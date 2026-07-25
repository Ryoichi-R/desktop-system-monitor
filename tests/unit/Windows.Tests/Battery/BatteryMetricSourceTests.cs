using System.Runtime.InteropServices;
using DesktopSystemMonitor.Core.Metrics;
using DesktopSystemMonitor.Windows.Battery;
using Xunit;

namespace DesktopSystemMonitor.Windows.Tests.Battery;

public sealed class BatteryMetricSourceTests
{
    [Fact]
    public void interop_layouts_are_stable()
    {
        Assert.Equal(12, Marshal.SizeOf<PowerStatusInterop.SystemPowerStatus>());
        Assert.Equal(32, Marshal.SizeOf<PowerStatusInterop.SystemBatteryStateValue>());
    }

    [Fact]
    public void maps_negative_rate_to_discharging()
    {
        var general = new PowerStatusInterop.SystemPowerStatus { AcLineStatus = 0, BatteryFlag = 2, BatteryLifePercent = 63 };
        var detailed = new PowerStatusInterop.SystemBatteryStateValue { BatteryPresent = 1, Discharging = 1, RemainingCapacity = 32_000, Rate = -8_000, EstimatedTime = 14_400 };
        BatterySnapshot result = BatteryMetricSource.Map(general, detailed);
        Assert.Equal(BatteryPowerState.Discharging, result.PowerState);
        Assert.Equal(MetricStatus.Ok, result.Status);
        Assert.Equal(-8_000, result.RateMilliwatts);
    }

    [Fact]
    public void sentinels_and_contradictions_are_unavailable()
    {
        var general = new PowerStatusInterop.SystemPowerStatus { BatteryLifePercent = byte.MaxValue };
        var detailed = new PowerStatusInterop.SystemBatteryStateValue { BatteryPresent = 1, Charging = 1, Discharging = 1, RemainingCapacity = uint.MaxValue, Rate = int.MinValue, EstimatedTime = uint.MaxValue };
        BatterySnapshot result = BatteryMetricSource.Map(general, detailed);
        Assert.Equal(BatteryPowerState.Unknown, result.PowerState);
        Assert.Equal(MetricStatus.Unavailable, result.Status);
        Assert.True(double.IsNaN(result.Percent));
        Assert.True(double.IsNaN(result.RateMilliwatts));
        Assert.Null(result.WindowsEstimatedTime);
    }

    [Fact]
    public async Task disabled_source_does_not_call_native_readers()
    {
        int calls = 0;
        using var source = new BatteryMetricSource(
            () =>
            {
                calls++;
                return (false, default);
            },
            () =>
            {
                calls++;
                return (false, default);
            });

        BatterySnapshot result = await source.SampleAsync(default);

        Assert.Equal(MetricStatus.Unavailable, result.Status);
        Assert.Equal(0, calls);
    }

    [Fact]
    public async Task enabled_source_combines_general_and_detailed_state()
    {
        var general = new PowerStatusInterop.SystemPowerStatus
        {
            AcLineStatus = 0,
            BatteryFlag = 2,
            BatteryLifePercent = 75,
        };
        var detailed = new PowerStatusInterop.SystemBatteryStateValue
        {
            BatteryPresent = 1,
            Discharging = 1,
            RemainingCapacity = 30_000,
            Rate = -6_000,
            EstimatedTime = 18_000,
        };
        using var source = new BatteryMetricSource(
            () => (true, general),
            () => (true, detailed));
        source.SetEnabled(true);

        BatterySnapshot result = await source.SampleAsync(default);

        Assert.Equal(75, result.Percent);
        Assert.Equal(30_000, result.RemainingCapacityMilliwattHours);
        Assert.Equal(TimeSpan.FromHours(5), result.WindowsEstimatedTime);
    }

    [Fact]
    public void maps_absent_and_ac_connected_states()
    {
        BatterySnapshot absent = BatteryMetricSource.Map(
            new PowerStatusInterop.SystemPowerStatus { BatteryFlag = 128 },
            new PowerStatusInterop.SystemBatteryStateValue());
        BatterySnapshot ac = BatteryMetricSource.Map(
            new PowerStatusInterop.SystemPowerStatus { AcLineStatus = 1, BatteryLifePercent = 90 },
            new PowerStatusInterop.SystemBatteryStateValue
            {
                BatteryPresent = 1,
                AcOnLine = 1,
                RemainingCapacity = 40_000,
                Rate = 0,
            });

        Assert.Equal(BatteryPowerState.Absent, absent.PowerState);
        Assert.Equal(BatteryPowerState.AcConnected, ac.PowerState);
        Assert.Equal(MetricStatus.Ok, ac.Status);
    }

    [Fact]
    public async Task native_reader_failure_is_unavailable()
    {
        using var source = new BatteryMetricSource(
            () => (false, default),
            () => (true, default));
        source.SetEnabled(true);

        BatterySnapshot result = await source.SampleAsync(default);

        Assert.Equal(MetricStatus.Unavailable, result.Status);
        Assert.False(result.BatteryPresent);
    }
}
