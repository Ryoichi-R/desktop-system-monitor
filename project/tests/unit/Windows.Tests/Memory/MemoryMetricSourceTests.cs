using DesktopSystemMonitor.Core.Metrics;
using DesktopSystemMonitor.Windows.Memory;
using Xunit;

namespace DesktopSystemMonitor.Windows.Tests.Memory;

public sealed class MemoryMetricSourceTests
{
    [Fact]
    public async Task sample_reports_used_total_and_utilization()
    {
        using var source = new MemoryMetricSource(() => new MemoryReading(16_000, 4_000));

        MemorySnapshot snapshot = await source.SampleAsync(default);

        Assert.Equal(MetricStatus.Ok, snapshot.Status);
        Assert.Equal(75, snapshot.UtilizationPercent);
        Assert.Equal(12_000, snapshot.UsedBytes);
        Assert.Equal(16_000, snapshot.TotalBytes);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(100, 101)]
    public async Task invalid_native_values_are_unavailable(ulong total, ulong available)
    {
        using var source = new MemoryMetricSource(() => new MemoryReading(total, available));

        MemorySnapshot snapshot = await source.SampleAsync(default);

        Assert.Equal(MetricStatus.Unavailable, snapshot.Status);
    }

    [Fact]
    public async Task native_failure_is_unavailable()
    {
        using var source = new MemoryMetricSource(() => null);

        MemorySnapshot snapshot = await source.SampleAsync(default);

        Assert.Equal(MetricStatus.Unavailable, snapshot.Status);
    }

    [Fact]
    public async Task total_larger_than_signed_snapshot_capacity_is_unavailable()
    {
        using var source = new MemoryMetricSource(() => new MemoryReading((ulong)long.MaxValue + 1, 0));

        MemorySnapshot snapshot = await source.SampleAsync(default);

        Assert.Equal(MetricStatus.Unavailable, snapshot.Status);
    }
}
