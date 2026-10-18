using DesktopSystemMonitor.Core.Metrics;

namespace DesktopSystemMonitor.Core.Sampling;

public readonly record struct BatteryChargeTimeEstimate(
    MetricStatus Status,
    TimeSpan? Remaining,
    double SmoothedChargeMilliwatts,
    int TargetPercent);

public sealed class BatteryChargeTimeEstimator
{
    private static readonly TimeSpan SampleWindow = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan MaximumGap = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan MaximumEstimate = TimeSpan.FromDays(7);
    private readonly Queue<(DateTimeOffset TakenAt, double Rate)> _samples = new();
    private BatteryPowerState? _lastState;
    private DateTimeOffset? _lastTakenAt;
    private int? _lastTargetPercent;

    public BatteryChargeTimeEstimate Add(DateTimeOffset takenAt, BatterySnapshot snapshot, int targetPercent)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        bool stateChanged = _lastState is not null && _lastState != snapshot.PowerState;
        bool longGap = _lastTakenAt is not null && takenAt - _lastTakenAt > MaximumGap;
        bool targetChanged = _lastTargetPercent is not null && _lastTargetPercent != targetPercent;
        _lastState = snapshot.PowerState;
        _lastTakenAt = takenAt;
        _lastTargetPercent = targetPercent;

        bool validInput = snapshot.PowerState == BatteryPowerState.Charging &&
            snapshot.Status == MetricStatus.Ok &&
            double.IsFinite(snapshot.RateMilliwatts) && snapshot.RateMilliwatts > 0 &&
            double.IsFinite(snapshot.RemainingCapacityMilliwattHours) && snapshot.RemainingCapacityMilliwattHours > 0 &&
            double.IsFinite(snapshot.Percent) && snapshot.Percent >= 5 &&
            targetPercent is >= 1 and <= 100 && snapshot.Percent < targetPercent;
        if (stateChanged || longGap || targetChanged)
        {
            _samples.Clear();
        }
        if (!validInput)
        {
            _samples.Clear();
            return new(MetricStatus.Unavailable, null, double.NaN, targetPercent);
        }

        _samples.Enqueue((takenAt, snapshot.RateMilliwatts));
        DateTimeOffset cutoff = takenAt - SampleWindow;
        while (_samples.TryPeek(out var sample) && sample.TakenAt < cutoff)
        {
            _samples.Dequeue();
        }

        if (_samples.Count < 5)
        {
            return new(MetricStatus.WarmingUp, null, double.NaN, targetPercent);
        }

        double[] rates = _samples.Select(sample => sample.Rate).Order().ToArray();
        double median = rates.Length % 2 == 1
            ? rates[rates.Length / 2]
            : (rates[(rates.Length / 2) - 1] + rates[rates.Length / 2]) / 2d;
        double neededCapacity = snapshot.RemainingCapacityMilliwattHours *
            (targetPercent - snapshot.Percent) / snapshot.Percent;
        if (!double.IsFinite(neededCapacity) || neededCapacity <= 0 || !double.IsFinite(median) || median <= 0)
        {
            return new(MetricStatus.Unavailable, null, double.NaN, targetPercent);
        }

        TimeSpan remaining = TimeSpan.FromHours(neededCapacity / median);
        if (remaining <= TimeSpan.Zero || remaining > MaximumEstimate)
        {
            return new(MetricStatus.Unavailable, null, double.NaN, targetPercent);
        }

        return new(MetricStatus.Ok, remaining, median, targetPercent);
    }

    public void Clear()
    {
        _samples.Clear();
        _lastState = null;
        _lastTakenAt = null;
        _lastTargetPercent = null;
    }
}
