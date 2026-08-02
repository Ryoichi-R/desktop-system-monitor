using DesktopSystemMonitor.Core.Metrics;

namespace DesktopSystemMonitor.Core.Sampling;

/// <summary>
/// Drives sampling on a fixed cadence without re-entrancy. When a sample is
/// still in flight and the timer ticks again, the tick is skipped.
/// </summary>
public sealed class SamplingOrchestrator : IAsyncDisposable
{
    private readonly Func<CancellationToken, ValueTask<MetricSnapshot>> _sample;
    private readonly Action<MetricSnapshot> _onSnapshot;
    private readonly Action<Exception> _onError;
    private readonly TimeSpan _interval;
    private readonly CancellationTokenSource _cts = new();
    private int _sampling;
    private int _paused;
    private Task? _loop;
    private Task? _activeSample;

    public SamplingOrchestrator(
        Func<CancellationToken, ValueTask<MetricSnapshot>> sample,
        Action<MetricSnapshot> onSnapshot,
        Action<Exception> onError,
        TimeSpan interval)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(interval, TimeSpan.Zero);
        _sample = sample ?? throw new ArgumentNullException(nameof(sample));
        _onSnapshot = onSnapshot ?? throw new ArgumentNullException(nameof(onSnapshot));
        _onError = onError ?? throw new ArgumentNullException(nameof(onError));
        _interval = interval;
    }

    public bool IsRunning => _loop is not null && !_loop.IsCompleted;
    public bool IsPaused => Volatile.Read(ref _paused) != 0;
    public int SkippedTicks => Volatile.Read(ref _skippedTicks);
    private int _skippedTicks;

    public void Start()
    {
        if (_loop is not null)
        {
            return;
        }
        _loop = Task.Run(() => RunAsync(_cts.Token));
    }

    public void Pause() => Interlocked.Exchange(ref _paused, 1);

    public void Resume() => Interlocked.Exchange(ref _paused, 0);

    private async Task RunAsync(CancellationToken token)
    {
        using var timer = new PeriodicTimer(_interval);
        while (!token.IsCancellationRequested)
        {
            try
            {
                if (!await timer.WaitForNextTickAsync(token).ConfigureAwait(false))
                {
                    return;
                }
            }
            catch (OperationCanceledException)
            {
                return;
            }

            if (IsPaused)
            {
                continue;
            }

            if (Interlocked.CompareExchange(ref _sampling, 1, 0) != 0)
            {
                Interlocked.Increment(ref _skippedTicks);
                continue;
            }

            _activeSample = SampleOnceAsync(token);
        }
    }

    private async Task SampleOnceAsync(CancellationToken token)
    {
        try
        {
            var snapshot = await _sample(token).ConfigureAwait(false);
            if (!token.IsCancellationRequested)
            {
                _onSnapshot(snapshot);
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            // Expected during shutdown.
        }
        catch (Exception ex)
        {
            _onError(ex);
        }
        finally
        {
            Interlocked.Exchange(ref _sampling, 0);
        }
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            await _cts.CancelAsync().ConfigureAwait(false);
        }
        catch
        {
            // ignore
        }
        if (_loop is not null)
        {
            try
            {
                await _loop.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // expected
            }
        }
        Task? active = _activeSample;
        if (active is not null)
        {
            try
            {
                await active.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // expected
            }
        }
        _cts.Dispose();
    }
}
