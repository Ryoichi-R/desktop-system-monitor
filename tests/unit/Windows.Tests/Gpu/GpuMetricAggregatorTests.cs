using DesktopSystemMonitor.Core.Metrics;
using DesktopSystemMonitor.Windows.Gpu;
using Xunit;

namespace DesktopSystemMonitor.Windows.Tests.Gpu;

[Trait("Category", "WindowsUnit")]
public sealed class GpuMetricAggregatorTests
{
    private static DxgiAdapterInfo Adapter(ulong luid, long limit = 1_000) =>
        new(luid, $"GPU {luid}", limit, 0, 0, false);

    [Fact]
    public void sums_processes_per_engine_and_uses_busiest_engine()
    {
        GpuSnapshot snapshot = GpuMetricAggregator.Aggregate(
            [
                new(1, 0, 0, "3D", 35),
                new(1, 0, 0, "3D", 45),
                new(1, 0, 1, "VideoDecode", 25),
            ],
            [new GpuMemoryReading(1, 400)],
            [Adapter(1)]);

        GpuAdapterSnapshot gpu = Assert.Single(snapshot.Adapters);
        Assert.Equal(80, gpu.UtilizationPercent);
        Assert.Equal("3D", gpu.BusiestEngineType);
        Assert.Equal(MetricStatus.Ok, gpu.MemoryStatus);
        Assert.Equal(400, gpu.DedicatedUsageBytes);
    }

    [Fact]
    public void clamps_aggregate_engine_utilization_to_one_hundred()
    {
        GpuSnapshot snapshot = GpuMetricAggregator.Aggregate(
            [new(1, 0, 0, "Compute", 80), new(1, 0, 0, "Compute", 90)],
            [],
            [Adapter(1)]);
        Assert.Equal(100, Assert.Single(snapshot.Adapters).UtilizationPercent);
    }

    [Theory]
    [InlineData(1_001, 1_000, false)]
    [InlineData(0, 0, true)]
    [InlineData(1, 0, false)]
    public void validates_memory_usage_against_dxgi_limit(long usage, long limit, bool expectedOk)
    {
        GpuSnapshot snapshot = GpuMetricAggregator.Aggregate(
            [],
            [new GpuMemoryReading(1, usage)],
            [Adapter(1, limit)]);
        Assert.Equal(expectedOk ? MetricStatus.Ok : MetricStatus.Unavailable, Assert.Single(snapshot.Adapters).MemoryStatus);
    }

    [Fact]
    public void missing_memory_counter_is_unavailable_instead_of_fabricated_zero()
    {
        GpuSnapshot snapshot = GpuMetricAggregator.Aggregate([], [], [Adapter(1)]);
        GpuAdapterSnapshot gpu = Assert.Single(snapshot.Adapters);
        Assert.Equal(MetricStatus.Unavailable, gpu.MemoryStatus);
        Assert.Equal(MetricStatus.Unavailable, gpu.UtilizationStatus);
        Assert.Equal("Idle", gpu.BusiestEngineType);
    }

    [Fact]
    public void missing_dxgi_match_keeps_usage_but_marks_memory_unavailable()
    {
        GpuSnapshot snapshot = GpuMetricAggregator.Aggregate(
            [new(7, 0, 0, "3D", 10)],
            [new GpuMemoryReading(7, 100)],
            []);
        GpuAdapterSnapshot gpu = Assert.Single(snapshot.Adapters);
        Assert.Equal(MetricStatus.Unavailable, gpu.MemoryStatus);
        Assert.Equal(-1, gpu.DedicatedLimitBytes);
    }

    [Fact]
    public void software_adapters_and_non_finite_readings_are_ignored()
    {
        var software = new DxgiAdapterInfo(2, "Software", 100, 0, 0, true);
        GpuSnapshot snapshot = GpuMetricAggregator.Aggregate(
            [new(1, 0, 0, "3D", double.NaN)],
            [new GpuMemoryReading(1, -1)],
            [software]);
        Assert.Empty(snapshot.Adapters);
        Assert.Equal(MetricStatus.Unavailable, snapshot.OverallStatus);
    }
}
