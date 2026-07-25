using DesktopSystemMonitor.Core.Metrics;
using DesktopSystemMonitor.Core.Utility;
using Xunit;

namespace DesktopSystemMonitor.Core.Tests.Metrics;

public sealed class SnapshotFactoryTests
{
    [Fact]
    public void cpu_factories_use_non_numeric_missing_values()
    {
        CpuSnapshot warmup = CpuSnapshot.Warmup();
        CpuSnapshot unavailable = CpuSnapshot.Unavailable();
        Assert.Equal(MetricStatus.WarmingUp, warmup.UtilizationStatus);
        Assert.Equal(MetricStatus.Unavailable, unavailable.FrequencyStatus);
        Assert.True(double.IsNaN(warmup.UtilizationPercent));
        Assert.True(double.IsNaN(unavailable.FrequencyMhz));
    }

    [Fact]
    public void network_factories_are_empty_and_have_explicit_status()
    {
        NetworkSnapshot warmup = NetworkSnapshot.Warmup();
        NetworkSnapshot unavailable = NetworkSnapshot.Unavailable();
        Assert.Empty(warmup.Adapters);
        Assert.Equal(MetricStatus.WarmingUp, warmup.AggregateStatus);
        Assert.Equal(MetricStatus.Unavailable, unavailable.AggregateStatus);
    }

    [Fact]
    public void gpu_unavailable_factory_is_empty()
    {
        GpuSnapshot snapshot = GpuSnapshot.Unavailable();
        Assert.Empty(snapshot.Adapters);
        Assert.Equal(MetricStatus.Unavailable, snapshot.OverallStatus);
        Assert.Null(snapshot.DisplayAdapter);
    }

    [Fact]
    public void memory_factories_use_non_numeric_missing_values()
    {
        MemorySnapshot warmup = MemorySnapshot.Warmup();
        MemorySnapshot unavailable = MemorySnapshot.Unavailable();
        Assert.Equal(MetricStatus.WarmingUp, warmup.Status);
        Assert.Equal(MetricStatus.Unavailable, unavailable.Status);
        Assert.True(double.IsNaN(warmup.UtilizationPercent));
        Assert.Equal(-1, unavailable.TotalBytes);
    }

    [Fact]
    public void power_factories_set_cpu_and_gpu_collection_states_independently_and_explicitly()
    {
        PowerSnapshot warmup = PowerSnapshot.Warmup();
        PowerSnapshot unavailable = PowerSnapshot.Unavailable();

        Assert.Equal(MetricStatus.WarmingUp, warmup.CpuPackageStatus);
        Assert.Equal(MetricStatus.WarmingUp, warmup.GpuCollectionStatus);
        Assert.Equal(MetricStatus.Unavailable, unavailable.CpuPackageStatus);
        Assert.Equal(MetricStatus.Unavailable, unavailable.GpuCollectionStatus);
        Assert.Empty(warmup.GpuReadings);
    }

    [Fact]
    public void optional_telemetry_factories_use_explicit_missing_states()
    {
        Assert.Equal(MetricStatus.WarmingUp, DiskSnapshot.Warmup().Status);
        Assert.Equal(BatteryPowerState.Absent, BatterySnapshot.Absent().PowerState);
        Assert.False(BatterySnapshot.Warmup().BatteryPresent);
        Assert.False(BatterySnapshot.Unavailable().BatteryPresent);
        Assert.Equal(MetricStatus.Unavailable, TemperatureSnapshot.Unavailable().CpuPackageStatus);
        Assert.Equal(MetricStatus.WarmingUp, HardwareTelemetrySnapshot.Warmup().Power.CpuPackageStatus);
    }

    [Fact]
    public void system_clock_is_monotonic_and_converts_ticks()
    {
        var clock = new SystemClock();
        long before = clock.GetTimestampTicks();
        long after = clock.GetTimestampTicks();
        Assert.True(after >= before);
        Assert.True(clock.UtcNow <= DateTimeOffset.UtcNow.AddSeconds(1));
        Assert.True(clock.TicksToSeconds(1) > 0);
    }
}
