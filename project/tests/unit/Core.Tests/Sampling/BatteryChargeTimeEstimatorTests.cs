using DesktopSystemMonitor.Core.Metrics;
using DesktopSystemMonitor.Core.Sampling;
using Xunit;

namespace DesktopSystemMonitor.Core.Tests.Sampling;

public sealed class BatteryChargeTimeEstimatorTests
{
    [Fact]
    public void fifth_valid_sample_estimates_time_to_target_from_current_capacity()
    {
        var estimator = new BatteryChargeTimeEstimator();
        DateTimeOffset start = DateTimeOffset.UtcNow;
        BatteryChargeTimeEstimate estimate = default;

        for (int index = 0; index < 5; index++)
        {
            estimate = estimator.Add(start.AddSeconds(index), Charging(50, 30_000, 12_000), 80);
        }

        Assert.Equal(MetricStatus.Ok, estimate.Status);
        Assert.Equal(TimeSpan.FromMinutes(90), estimate.Remaining);
        Assert.Equal(12_000, estimate.SmoothedChargeMilliwatts);
        Assert.Equal(80, estimate.TargetPercent);
    }

    [Fact]
    public void uses_median_and_ignores_windows_runtime_value()
    {
        var estimator = new BatteryChargeTimeEstimator();
        DateTimeOffset start = DateTimeOffset.UtcNow;
        double[] rates = [12_000, 12_000, 80_000, 11_000, 13_000];
        BatteryChargeTimeEstimate estimate = default;

        for (int index = 0; index < rates.Length; index++)
        {
            estimate = estimator.Add(
                start.AddSeconds(index),
                Charging(50, 30_000, rates[index]) with { WindowsEstimatedTime = TimeSpan.FromMinutes(1) },
                80);
        }

        Assert.Equal(MetricStatus.Ok, estimate.Status);
        Assert.Equal(12_000, estimate.SmoothedChargeMilliwatts);
        Assert.Equal(TimeSpan.FromMinutes(90), estimate.Remaining);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(4)]
    [InlineData(80)]
    [InlineData(81)]
    public void rejects_percent_outside_estimation_range(double percent)
    {
        var estimator = new BatteryChargeTimeEstimator();

        BatteryChargeTimeEstimate estimate = estimator.Add(
            DateTimeOffset.UtcNow,
            Charging(percent, 30_000, 12_000),
            80);

        Assert.Equal(MetricStatus.Unavailable, estimate.Status);
    }

    [Fact]
    public void accepts_five_percent_and_warms_up()
    {
        var estimator = new BatteryChargeTimeEstimator();

        BatteryChargeTimeEstimate estimate = estimator.Add(
            DateTimeOffset.UtcNow,
            Charging(5, 3_000, 12_000),
            80);

        Assert.Equal(MetricStatus.WarmingUp, estimate.Status);
    }

    [Fact]
    public void target_change_clears_existing_samples()
    {
        var estimator = new BatteryChargeTimeEstimator();
        DateTimeOffset start = DateTimeOffset.UtcNow;
        for (int index = 0; index < 5; index++)
        {
            estimator.Add(start.AddSeconds(index), Charging(50, 30_000, 12_000), 80);
        }

        BatteryChargeTimeEstimate estimate = estimator.Add(
            start.AddSeconds(5),
            Charging(50, 30_000, 12_000),
            100);

        Assert.Equal(MetricStatus.WarmingUp, estimate.Status);
    }

    [Fact]
    public void invalid_rate_and_long_gap_clear_existing_samples()
    {
        var estimator = new BatteryChargeTimeEstimator();
        DateTimeOffset start = DateTimeOffset.UtcNow;
        for (int index = 0; index < 4; index++)
        {
            estimator.Add(start.AddSeconds(index), Charging(50, 30_000, 12_000), 80);
        }

        Assert.Equal(
            MetricStatus.Unavailable,
            estimator.Add(start.AddSeconds(4), Charging(50, 30_000, 0), 80).Status);
        Assert.Equal(
            MetricStatus.WarmingUp,
            estimator.Add(start.AddSeconds(20), Charging(50, 30_000, 12_000), 80).Status);
    }

    [Fact]
    public void sample_window_evicts_old_rates_without_resetting_recent_samples()
    {
        var estimator = new BatteryChargeTimeEstimator();
        DateTimeOffset start = DateTimeOffset.UtcNow;
        BatteryChargeTimeEstimate estimate = default;

        for (int index = 0; index <= 7; index++)
        {
            estimate = estimator.Add(
                start.AddSeconds(index * 10),
                Charging(50, 30_000, 12_000),
                80);
        }

        Assert.Equal(MetricStatus.Ok, estimate.Status);
        Assert.Equal(12_000, estimate.SmoothedChargeMilliwatts);
    }

    [Fact]
    public void explicit_clear_discards_accumulated_samples_and_target()
    {
        var estimator = new BatteryChargeTimeEstimator();
        DateTimeOffset start = DateTimeOffset.UtcNow;
        for (int index = 0; index < 5; index++)
        {
            estimator.Add(start.AddSeconds(index), Charging(50, 30_000, 12_000), 80);
        }

        estimator.Clear();
        BatteryChargeTimeEstimate estimate = estimator.Add(
            start.AddSeconds(5),
            Charging(50, 30_000, 12_000),
            100);

        Assert.Equal(MetricStatus.WarmingUp, estimate.Status);
    }

    private static BatterySnapshot Charging(double percent, double capacity, double rate) => new()
    {
        Status = MetricStatus.Ok,
        PowerState = BatteryPowerState.Charging,
        BatteryPresent = true,
        Percent = percent,
        RemainingCapacityMilliwattHours = capacity,
        RateMilliwatts = rate,
        WindowsEstimatedTime = null,
    };
}
