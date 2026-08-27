using DesktopSystemMonitor.Core.Metrics;
using DesktopSystemMonitor.Mac;

using Xunit;

namespace DesktopSystemMonitor.Mac.Tests;

public sealed class MacMetricSourceFactoryTests
{
    [Fact]
    public async Task Unimplemented_Native_Sources_Are_Fail_Safe()
    {
        var factory = new MacMetricSourceFactory();
        using IMetricSource<CpuSnapshot> cpu = factory.CreateCpu();
        using IMetricSource<MemorySnapshot> memory = factory.CreateMemory();
        using IGpuMetricSource gpu = factory.CreateGpu();
        using INetworkMetricSource network = factory.CreateNetwork();
        using IPowerMetricSource power = factory.CreatePower((_, _) => { });
        using IDiskMetricSource disk = factory.CreateDisk();

        Assert.Equal(MetricStatus.Unavailable, (await cpu.SampleAsync(CancellationToken.None)).UtilizationStatus);
        Assert.Equal(MetricStatus.Unavailable, (await memory.SampleAsync(CancellationToken.None)).Status);
        Assert.Equal(MetricStatus.Unavailable, (await gpu.SampleAsync(CancellationToken.None)).OverallStatus);
        Assert.Equal(MetricStatus.Unavailable, (await network.SampleAsync(CancellationToken.None)).AggregateStatus);
        Assert.Equal(MetricStatus.Unavailable, (await power.SampleAsync(CancellationToken.None)).CpuPackageStatus);
        Assert.Equal(MetricStatus.Unavailable, (await disk.SampleAsync(CancellationToken.None)).Status);
    }
}
