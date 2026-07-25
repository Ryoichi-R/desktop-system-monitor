using System;
using DesktopSystemMonitor.Core.Metrics;
using Xunit;

namespace DesktopSystemMonitor.App.Tests;

public sealed class OptionalTelemetryCapabilitiesTests
{
    [Fact]
    public void reports_supported_sources_from_an_ok_snapshot()
    {
        MetricSnapshot snapshot = MetricSnapshot.Warmup(DateTimeOffset.UtcNow) with
        {
            Temperature = new TemperatureSnapshot
            {
                CpuPackageStatus = MetricStatus.Ok,
                CpuPackageCelsius = 50,
                GpuCollectionStatus = MetricStatus.Unavailable,
                GpuReadings = [],
            },
            Battery = new BatterySnapshot
            {
                Status = MetricStatus.Ok,
                PowerState = BatteryPowerState.Discharging,
                BatteryPresent = true,
                Percent = 50,
                RemainingCapacityMilliwattHours = 20_000,
                RateMilliwatts = -5_000,
                WindowsEstimatedTime = null,
            },
        };

        OptionalTelemetryCapabilities result = OptionalTelemetryCapabilities.FromSnapshot(snapshot);

        Assert.Equal(OptionalTelemetryCapabilityStatus.Supported, result.CpuTemperature);
        Assert.Equal(OptionalTelemetryCapabilityStatus.Unavailable, result.GpuTemperature);
        Assert.Equal(OptionalTelemetryCapabilityStatus.Supported, result.Battery);
    }

    [Fact]
    public void distinguishes_not_detected_from_unavailable()
    {
        MetricSnapshot snapshot = MetricSnapshot.Warmup(DateTimeOffset.UtcNow) with
        {
            Temperature = TemperatureSnapshot.Unavailable(),
            Battery = BatterySnapshot.Absent(),
        };

        OptionalTelemetryCapabilities result = OptionalTelemetryCapabilities.FromSnapshot(snapshot);

        Assert.Equal("取得不可", OptionalTelemetryCapabilities.Display(result.CpuTemperature));
        Assert.Equal("取得不可", OptionalTelemetryCapabilities.Display(result.GpuTemperature));
        Assert.Equal("未検出", OptionalTelemetryCapabilities.Display(result.Battery));
    }
}
