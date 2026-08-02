using DesktopSystemMonitor.Core.Metrics;

namespace DesktopSystemMonitor.Core.Sampling;

public enum BatteryRuntimeEstimateSource
{
    None,
    Windows,
    Calculated,
}

public readonly record struct BatteryRuntimeEstimate(MetricStatus Status, TimeSpan? Remaining, double SmoothedDischargeMilliwatts, BatteryRuntimeEstimateSource Source);

public sealed class BatteryRuntimeEstimator
{
    private readonly Queue<(DateTimeOffset TakenAt, double Rate)> _samples = new();
    private BatteryPowerState? _lastState;
    private DateTimeOffset? _lastTakenAt;

    public BatteryRuntimeEstimate Add(DateTimeOffset takenAt, BatterySnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        bool stateChanged = _lastState is not null && _lastState != snapshot.PowerState;
        bool longGap = _lastTakenAt is not null && takenAt - _lastTakenAt > TimeSpan.FromSeconds(10);
        _lastState = snapshot.PowerState;
        _lastTakenAt = takenAt;

        if (stateChanged || longGap || snapshot.PowerState != BatteryPowerState.Discharging ||
            snapshot.Status != MetricStatus.Ok || !double.IsFinite(snapshot.RateMilliwatts) || snapshot.RateMilliwatts >= 0)
        {
            _samples.Clear();
            return snapshot.PowerState == BatteryPowerState.Discharging && snapshot.WindowsEstimatedTime is { } fallback
                ? new(MetricStatus.WarmingUp, fallback, double.NaN, BatteryRuntimeEstimateSource.Windows)
                : new(MetricStatus.Unavailable, null, double.NaN, BatteryRuntimeEstimateSource.None);
        }

        _samples.Enqueue((takenAt, Math.Abs(snapshot.RateMilliwatts)));
        DateTimeOffset cutoff = takenAt - TimeSpan.FromSeconds(60);
        while (_samples.TryPeek(out var sample) && sample.TakenAt < cutoff)
        {
            _samples.Dequeue();
        }

        if (_samples.Count < 5)
        {
            return snapshot.WindowsEstimatedTime is { } fallback
                ? new(MetricStatus.WarmingUp, fallback, double.NaN, BatteryRuntimeEstimateSource.Windows)
                : new(MetricStatus.WarmingUp, null, double.NaN, BatteryRuntimeEstimateSource.None);
        }

        double[] rates = _samples.Select(sample => sample.Rate).Order().ToArray();
        double median = rates.Length % 2 == 1
            ? rates[rates.Length / 2]
            : (rates[(rates.Length / 2) - 1] + rates[rates.Length / 2]) / 2d;
        if (!double.IsFinite(snapshot.RemainingCapacityMilliwattHours) || snapshot.RemainingCapacityMilliwattHours <= 0 || median <= 0)
        {
            return new(MetricStatus.Unavailable, null, double.NaN, BatteryRuntimeEstimateSource.None);
        }
        TimeSpan remaining = TimeSpan.FromHours(snapshot.RemainingCapacityMilliwattHours / median);
        if (remaining <= TimeSpan.Zero || remaining > TimeSpan.FromDays(7))
        {
            return new(MetricStatus.Unavailable, null, double.NaN, BatteryRuntimeEstimateSource.None);
        }
        return new(MetricStatus.Ok, remaining, median, BatteryRuntimeEstimateSource.Calculated);
    }

    public void Clear()
    {
        _samples.Clear();
        _lastState = null;
        _lastTakenAt = null;
    }
}
