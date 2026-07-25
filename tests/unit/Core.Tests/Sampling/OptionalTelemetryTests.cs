using DesktopSystemMonitor.Core.Metrics;
using DesktopSystemMonitor.Core.Sampling;
using Xunit;

namespace DesktopSystemMonitor.Core.Tests.Sampling;

public sealed class OptionalTelemetryTests
{
    [Fact]
    public void peak_tracker_uses_time_window_and_rejects_out_of_order()
    {
        var tracker = new RecentPeakTracker();
        DateTimeOffset start = DateTimeOffset.UtcNow;
        Assert.True(tracker.Add(start, 10));
        Assert.True(tracker.Add(start.AddSeconds(30), 20));
        Assert.True(tracker.Add(start.AddSeconds(61), 5));
        Assert.Equal(20, tracker.Peak);
        Assert.False(tracker.Add(start, 100));
    }

    [Fact]
    public void battery_estimator_uses_median_after_five_samples()
    {
        var estimator = new BatteryRuntimeEstimator();
        DateTimeOffset start = DateTimeOffset.UtcNow;
        double[] rates = [-8_000, -8_000, -40_000, -8_000, -8_000];
        BatteryRuntimeEstimate estimate = default;
        for (int index = 0; index < rates.Length; index++)
        {
            estimate = estimator.Add(start.AddSeconds(index), Discharging(rates[index]));
        }
        Assert.Equal(MetricStatus.Ok, estimate.Status);
        Assert.Equal(BatteryRuntimeEstimateSource.Calculated, estimate.Source);
        Assert.Equal(TimeSpan.FromHours(4), estimate.Remaining);
        Assert.Equal(8_000, estimate.SmoothedDischargeMilliwatts);
    }

    [Fact]
    public void battery_estimator_resets_on_state_transition()
    {
        var estimator = new BatteryRuntimeEstimator();
        DateTimeOffset start = DateTimeOffset.UtcNow;
        for (int index = 0; index < 5; index++)
        {
            _ = estimator.Add(start.AddSeconds(index), Discharging(-8_000));
        }
        BatteryRuntimeEstimate result = estimator.Add(start.AddSeconds(6), BatterySnapshot.Unavailable() with
        {
            BatteryPresent = true,
            PowerState = BatteryPowerState.Charging,
        });
        Assert.Equal(MetricStatus.Unavailable, result.Status);
    }

    private static BatterySnapshot Discharging(double rate) => new()
    {
        Status = MetricStatus.Ok,
        PowerState = BatteryPowerState.Discharging,
        BatteryPresent = true,
        Percent = 63,
        RemainingCapacityMilliwattHours = 32_000,
        RateMilliwatts = rate,
        WindowsEstimatedTime = null,
    };
}
