using System.IO;
using System.Threading.Channels;
using DesktopSystemMonitor.Windows.Window;

namespace DesktopSystemMonitor.App.Diagnostics;

/// <summary>
/// Keeps a bounded in-memory event history and optionally writes a small,
/// rotating local log. Callers pass event categories and exception types only;
/// metric values, process names, and full PDH instance paths are never logged.
/// File I/O is serialized on a dedicated writer and never runs under the
/// in-memory history lock.
/// </summary>
public sealed class DiagnosticLog : IDisposable
{
    private const int Capacity = 200;
    private const int FileQueueCapacity = 256;
    private const long MaxFileBytes = 1_000_000;
    private const int FileCount = 5;
    private static readonly TimeSpan DisposeFlushTimeout = TimeSpan.FromSeconds(1);

    private readonly object _gate = new();
    private readonly Queue<string> _events = new();
    private readonly string _directory;
    private readonly Channel<string> _pending;
    private readonly Task _writer;
    private readonly Func<string, Task>? _writeOverride;
    private int _enabled;
    private int _completed;
    private long _droppedFileRecordCount;

    public DiagnosticLog(string directory, bool enabled)
        : this(directory, enabled, FileQueueCapacity, writeOverride: null)
    {
    }

    internal DiagnosticLog(
        string directory,
        bool enabled,
        int fileQueueCapacity,
        Func<string, Task>? writeOverride)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(fileQueueCapacity);
        _directory = directory;
        _writeOverride = writeOverride;
        _pending = Channel.CreateBounded<string>(new BoundedChannelOptions(fileQueueCapacity)
        {
            SingleReader = true,
            SingleWriter = false,
            FullMode = BoundedChannelFullMode.Wait,
        });
        _enabled = enabled ? 1 : 0;
        _writer = Task.Run(WriteLoopAsync);
    }

    public long DroppedFileRecordCount => Interlocked.Read(ref _droppedFileRecordCount);

    public bool Enabled
    {
        get => Volatile.Read(ref _enabled) != 0;
        set => Volatile.Write(ref _enabled, value ? 1 : 0);
    }

    public IReadOnlyList<string> Snapshot()
    {
        lock (_gate)
        {
            return _events.ToArray();
        }
    }

    public void Record(string category, Exception? exception = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(category);
        string suffix = exception is null
            ? string.Empty
            : $" exception={exception.GetType().Name} hresult=0x{exception.HResult:X8}";
        RecordLine($"{DateTimeOffset.UtcNow:O} category={category}{suffix}");
    }

    internal void RecordLayerEvent(LayerDiagnosticEvent diagnosticEvent)
    {
        string category = diagnosticEvent.Category switch
        {
            LayerDiagnosticCategory.ApplyFailure => "layer-apply-failure",
            LayerDiagnosticCategory.RepairEnter => "layer-repair-enter",
            LayerDiagnosticCategory.RepairRecovered => "layer-repair-recovered",
            LayerDiagnosticCategory.ManagedException => "layer-managed-exception",
            LayerDiagnosticCategory.FallbackSubscriberFailure => "layer-fallback-callback-failure",
            _ => "layer-unknown",
        };
        string error = diagnosticEvent.ErrorCode is int errorCode
            ? errorCode.ToString(System.Globalization.CultureInfo.InvariantCulture)
            : "none";
        string line = $"{DateTimeOffset.UtcNow:O} category={category}"
            + $" desired={diagnosticEvent.DesiredStrategy}"
            + $" effective={diagnosticEvent.EffectiveState}"
            + $" trigger={diagnosticEvent.Trigger}"
            + $" failure={diagnosticEvent.FailureKind}"
            + $" error={error}"
            + $" consecutive={diagnosticEvent.ConsecutiveFailures}";
        RecordLine(line);
    }

    private void RecordLine(string line)
    {
        lock (_gate)
        {
            _events.Enqueue(line);
            while (_events.Count > Capacity)
            {
                _events.Dequeue();
            }
        }

        if (Enabled)
        {
            if (Volatile.Read(ref _completed) != 0 || !_pending.Writer.TryWrite(line))
            {
                Interlocked.Increment(ref _droppedFileRecordCount);
            }
        }
    }

    internal async Task CompleteAsync(TimeSpan timeout)
    {
        if (Interlocked.Exchange(ref _completed, 1) == 0)
        {
            _pending.Writer.TryComplete();
        }
        await _writer.WaitAsync(timeout).ConfigureAwait(false);
    }

    private async Task WriteLoopAsync()
    {
        await foreach (string line in _pending.Reader.ReadAllAsync().ConfigureAwait(false))
        {
            try
            {
                if (_writeOverride is not null)
                {
                    await _writeOverride(line).ConfigureAwait(false);
                    continue;
                }
                Directory.CreateDirectory(_directory);
                string current = Path.Combine(_directory, "desktop-system-monitor.log");
                if (File.Exists(current) && new FileInfo(current).Length >= MaxFileBytes)
                {
                    Rotate(current);
                }
                await File.AppendAllTextAsync(current, line + Environment.NewLine).ConfigureAwait(false);
            }
            catch
            {
                // Diagnostics must never destabilize the monitor.
            }
        }
    }

    private static void Rotate(string current)
    {
        string oldest = current + $".{FileCount - 1}";
        if (File.Exists(oldest))
        {
            File.Delete(oldest);
        }
        for (int index = FileCount - 2; index >= 1; index--)
        {
            string source = current + $".{index}";
            if (File.Exists(source))
            {
                File.Move(source, current + $".{index + 1}");
            }
        }
        File.Move(current, current + ".1");
    }

    public void Dispose()
    {
        try
        {
            CompleteAsync(DisposeFlushTimeout).GetAwaiter().GetResult();
        }
        catch (TimeoutException)
        {
            // Shutdown is bounded even if storage is unavailable or very slow.
        }
    }
}
