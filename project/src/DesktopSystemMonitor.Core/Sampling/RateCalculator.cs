namespace DesktopSystemMonitor.Core.Sampling;

/// <summary>
/// Computes bytes/sec (or similar rate) from monotonically increasing counter
/// snapshots. Handles first sample (warmup), zero elapsed, counter reset
/// (source restart), and 32/64-bit counter wrap.
/// </summary>
public sealed class RateCalculator
{
    private readonly long _wrapBoundary;
    private readonly bool _hasBoundedWrap;
    private long? _lastValue;
    private long? _lastTimestampTicks;
    private long _sessionAccumulated;

    public RateCalculator(long wrapBoundary = long.MaxValue)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(wrapBoundary);
        _wrapBoundary = wrapBoundary;
        _hasBoundedWrap = wrapBoundary != long.MaxValue;
    }

    public long SessionAccumulated => _sessionAccumulated;
    public bool HasBaseline => _lastValue.HasValue;

    public void Reset()
    {
        _lastValue = null;
        _lastTimestampTicks = null;
        _sessionAccumulated = 0;
    }

    public RateSample Update(long currentValue, long timestampTicks, double ticksPerSecond)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(currentValue);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(ticksPerSecond);

        if (_lastValue is not long lastValue || _lastTimestampTicks is not long lastTicks)
        {
            _lastValue = currentValue;
            _lastTimestampTicks = timestampTicks;
            return RateSample.WarmingUp;
        }

        long elapsedTicks = timestampTicks - lastTicks;
        if (elapsedTicks <= 0)
        {
            return RateSample.WarmingUp;
        }

        long delta;
        if (currentValue >= lastValue)
        {
            delta = currentValue - lastValue;
        }
        else
        {
            if (!_hasBoundedWrap)
            {
                // 64-bit source counters do not wrap in practice; treat any
                // decrease as an interface / driver restart and rebaseline.
                _lastValue = currentValue;
                _lastTimestampTicks = timestampTicks;
                return RateSample.WarmingUp;
            }
            long wrapRemainder = _wrapBoundary - lastValue;
            long wrapDelta = wrapRemainder + currentValue;
            long deficit = lastValue - currentValue;
            if (deficit > _wrapBoundary / 4 && wrapDelta > _wrapBoundary / 4)
            {
                // Both interpretations produce a large jump — safer to treat as
                // a reset (0 delta) than to publish a spike.
                _lastValue = currentValue;
                _lastTimestampTicks = timestampTicks;
                return RateSample.WarmingUp;
            }
            delta = wrapDelta;
        }

        double seconds = elapsedTicks / ticksPerSecond;
        double rate = delta / seconds;
        _lastValue = currentValue;
        _lastTimestampTicks = timestampTicks;
        _sessionAccumulated += delta;
        return new RateSample(rate, delta, seconds);
    }
}

public readonly record struct RateSample(double RatePerSecond, long Delta, double ElapsedSeconds)
{
    public static readonly RateSample WarmingUp = new(double.NaN, 0, 0);
    public bool IsWarmingUp => double.IsNaN(RatePerSecond);
}
