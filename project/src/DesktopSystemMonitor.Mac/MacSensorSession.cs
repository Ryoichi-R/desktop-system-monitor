using System.Diagnostics;
using DesktopSystemMonitor.Core.Platform;

namespace DesktopSystemMonitor.Mac;

internal interface ISensorHostConnection : IAsyncDisposable
{
    void Abort();
    ValueTask<SensorHostMessage?> ExchangeAsync(SensorHostMessage request, CancellationToken cancellationToken);
}

public enum MacSensorConnectionState { Connecting, Connected, RetryWaiting, RetryStopped, Closed }
public enum MacSensorFailure { None, TimedOut, InvalidResponse, ConnectionFailed }
public sealed record MacSensorConnectionStatus(MacSensorConnectionState State, MacSensorFailure Failure = MacSensorFailure.None, int Attempts = 0);

public interface IMacSensorSession : IAsyncDisposable
{
    MacSensorConnectionStatus Status { get; }
    ValueTask<SensorHostMetrics> SampleAsync(CancellationToken cancellationToken, bool includeHighLoadProcesses = false,
        bool resetHighLoadProcesses = false);
    ValueTask ReconnectAsync();
    void StopPolling();
}

/// <summary>One serial, timeout-bounded IPC session. Native metrics never run in the UI process.</summary>
public sealed class MacSensorSession : IMacSensorSession
{
    private readonly Func<ISensorHostConnection> _connect;
    private readonly Func<long> _clock;
    private readonly TimeSpan _timeout;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly CancellationTokenSource _lifetime = new();
    private volatile ISensorHostConnection? _connection;
    private readonly object _exchangeLock = new();
    private CancellationTokenSource? _exchange;
    private int _reconnecting;
    private volatile MacSensorConnectionStatus _status = new(MacSensorConnectionState.Connecting);
    public MacSensorConnectionStatus Status => _lifetime.IsCancellationRequested
        ? new(MacSensorConnectionState.Closed)
        : _status;
    private long _sequence, _generation, _nextAttempt;
    private int _failures;
    private bool _disposed;

    public MacSensorSession() : this(CreateBundledConnection, Stopwatch.GetTimestamp, TimeSpan.FromSeconds(2)) { }

    internal MacSensorSession(Func<ISensorHostConnection> connect, Func<long> clock, TimeSpan timeout)
    {
        _connect = connect;
        _clock = clock;
        _timeout = timeout;
    }

    public async ValueTask<SensorHostMetrics> SampleAsync(CancellationToken cancellationToken, bool includeHighLoadProcesses = false,
        bool resetHighLoadProcesses = false)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
        await _gate.WaitAsync(linked.Token).ConfigureAwait(false);
        try
        {
            if (_disposed || _failures >= 5 || _clock() < _nextAttempt) return SensorHostMetrics.Empty;
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(linked.Token);
            lock (_exchangeLock)
            {
                if (Volatile.Read(ref _reconnecting) != 0) return SensorHostMetrics.Empty;
                _exchange = deadline;
            }
            deadline.CancelAfter(_timeout);
            try
            {
                if (_connection is null)
                {
                    _connection = _connect();
                    _generation++;
                }
                long sequence = ++_sequence;
                var request = SensorHostProtocol.Unavailable(sequence, _generation, "sample-request") with
                {
                    Kind = "sample",
                    Status = "ok",
                    ErrorCode = null,
                    IncludeHighLoadProcesses = includeHighLoadProcesses,
                    ResetHighLoadProcesses = resetHighLoadProcesses,
                };
                SensorHostMessage response = await _connection.ExchangeAsync(request, deadline.Token).ConfigureAwait(false)
                    ?? throw new InvalidDataException("SensorHost closed its output.");
                SensorHostProtocol.ValidateResponse(response, sequence, _generation, Stopwatch.GetTimestamp(), Stopwatch.Frequency, _timeout);
                deadline.Token.ThrowIfCancellationRequested();
                _failures = 0;
                _status = new(MacSensorConnectionState.Connected);
                return response.Status == "ok" ? response.Metrics : SensorHostMetrics.Empty;
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or InvalidOperationException or OperationCanceledException or System.ComponentModel.Win32Exception or UnauthorizedAccessException)
            {
                await DisconnectAsync().ConfigureAwait(false);
                if (linked.IsCancellationRequested) throw new OperationCanceledException(linked.Token);
                if (Volatile.Read(ref _reconnecting) != 0) return SensorHostMetrics.Empty;
                _failures++;
                var failure = ex switch
                {
                    OperationCanceledException => MacSensorFailure.TimedOut,
                    InvalidDataException => MacSensorFailure.InvalidResponse,
                    _ => MacSensorFailure.ConnectionFailed,
                };
                _status = new(_failures >= 5 ? MacSensorConnectionState.RetryStopped : MacSensorConnectionState.RetryWaiting, failure, _failures);
                _nextAttempt = _clock() + (long)(Math.Pow(2, _failures - 1) * Stopwatch.Frequency);
                return SensorHostMetrics.Empty;
            }
            finally
            {
                lock (_exchangeLock) _exchange = null;
            }
        }
        finally { _gate.Release(); }
    }

    public async ValueTask ReconnectAsync()
    {
        if (_lifetime.IsCancellationRequested || Interlocked.Exchange(ref _reconnecting, 1) != 0) return;
        try
        {
            _status = new(MacSensorConnectionState.Connecting);
            lock (_exchangeLock) _exchange?.Cancel();
            await _gate.WaitAsync().ConfigureAwait(false);
            try
            {
                await DisconnectAsync().ConfigureAwait(false);
                _failures = 0;
                _nextAttempt = 0;
            }
            finally { _gate.Release(); }
        }
        finally { Volatile.Write(ref _reconnecting, 0); }
    }

    public void StopPolling()
    {
        _lifetime.Cancel();
        _connection?.Abort();
    }

    public async ValueTask DisposeAsync()
    {
        StopPolling();
        await _lifetime.CancelAsync().ConfigureAwait(false);
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_disposed) return;
            _disposed = true;
            await DisconnectAsync().ConfigureAwait(false);
        }
        finally { _gate.Release(); }
        // Synchronization objects remain available for late UI callbacks after cancellation.
        GC.SuppressFinalize(this);
    }

    private async ValueTask DisconnectAsync()
    {
        var connection = _connection;
        _connection = null;
        if (connection is not null) await connection.DisposeAsync().ConfigureAwait(false);
    }

    private static ISensorHostConnection CreateBundledConnection()
    {
        if (!OperatingSystem.IsMacOS()) throw new InvalidOperationException("macOS is required.");
        string executable = Environment.ProcessPath ?? throw new InvalidOperationException("App path unavailable.");
        string directory = Path.GetDirectoryName(executable)!;
        string root = Path.GetFullPath(Path.Combine(directory, "..", ".."));
        string candidate = Path.Combine(directory, SensorHostPathResolver.SensorHostFileName);
        string validated = SensorHostPathResolver.Resolve(root, candidate, new RealSensorHostFileSystem());
        return new ProcessSensorHostConnection(validated);
    }
}

internal sealed class ProcessSensorHostConnection : ISensorHostConnection
{
    private readonly Process _process;
    private readonly Task _stderrDrain;

    internal ProcessSensorHostConnection(string absolutePath)
    {
        _process = new Process
        {
            StartInfo = new ProcessStartInfo(absolutePath)
            {
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            },
        };
        try
        {
            if (!_process.Start()) throw new IOException("SensorHost failed to start.");
            _stderrDrain = _process.StandardError.BaseStream.CopyToAsync(Stream.Null);
        }
        catch { _process.Dispose(); throw; }
    }

    public async ValueTask<SensorHostMessage?> ExchangeAsync(SensorHostMessage request, CancellationToken cancellationToken)
    {
        await SensorHostProtocol.WriteAsync(_process.StandardInput.BaseStream, request, cancellationToken).ConfigureAwait(false);
        return await SensorHostProtocol.ReadAsync(_process.StandardOutput.BaseStream, cancellationToken).ConfigureAwait(false);
    }

    public void Abort()
    {
        try { if (!_process.HasExited) _process.Kill(entireProcessTree: true); }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception) { }
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            if (!_process.HasExited) _process.Kill(entireProcessTree: true);
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            await _process.WaitForExitAsync(deadline.Token).ConfigureAwait(false);
            await _stderrDrain.WaitAsync(deadline.Token).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or OperationCanceledException or System.ComponentModel.Win32Exception) { }
        finally { _process.Dispose(); }
    }
}
