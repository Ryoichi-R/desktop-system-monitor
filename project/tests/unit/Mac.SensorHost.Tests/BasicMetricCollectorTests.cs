using DesktopSystemMonitor.Core.Platform;
using Xunit;

namespace DesktopSystemMonitor.Mac.SensorHost.Tests;

public sealed class BasicMetricCollectorTests
{
    [Fact]
    public void Cpu_Uses_Deltas_Including_Uint_Wrap()
    {
        Assert.Equal(50, BasicMetricCollector.CpuPercent([10, 10, 10, 10], [15, 15, 25, 15]));
        Assert.Equal(50, BasicMetricCollector.CpuPercent([uint.MaxValue - 2, 0, 0, 0], [2, 0, 5, 0]));
    }

    [Fact]
    public void Cpu_Rejects_Reset_Empty_Interval_And_Wrong_Length()
    {
        Assert.Null(BasicMetricCollector.CpuPercent([20, 20, 20, 20], [10, 10, 10, 10]));
        Assert.Null(BasicMetricCollector.CpuPercent([1, 2, 3, 4], [1, 2, 3, 4]));
        Assert.Null(BasicMetricCollector.CpuPercent([], [1, 2, 3, 4]));
    }

    [Fact]
    public void First_Sample_Warms_Cpu_While_Memory_Is_Usable()
    {
        var reader = new Reader();
        var collector = new BasicMetricCollector(reader);
        var first = collector.Sample();
        Assert.Equal(SensorHostMetricStatus.WarmingUp, first.CpuUtilizationPercent.Status);
        Assert.Equal(25, first.MemoryUtilizationPercent.Value);
        reader.Ticks = [15, 15, 25, 15];
        Assert.Equal(50, collector.Sample().CpuUtilizationPercent.Value);
    }

    [Fact]
    public void Failed_Cpu_Read_Resets_Baseline_And_Does_Not_Lose_Memory()
    {
        var reader = new Reader();
        var collector = new BasicMetricCollector(reader);
        collector.Sample();
        reader.FailCpu = true;
        var failed = collector.Sample();
        Assert.Equal(SensorHostMetricStatus.Unavailable, failed.CpuUtilizationPercent.Status);
        Assert.Equal(25, failed.MemoryUtilizationPercent.Value);
        reader.FailCpu = false;
        Assert.Equal(SensorHostMetricStatus.WarmingUp, collector.Sample().CpuUtilizationPercent.Status);
    }

    [Theory]
    [InlineData(1UL, 0UL)]
    [InlineData(11UL, 10UL)]
    public void Invalid_Memory_Is_Not_Reported_As_Zero(ulong used, ulong total)
    {
        var collector = new BasicMetricCollector(new Reader { Used = used, Total = total });
        Assert.Equal(SensorHostMetricStatus.Unavailable, collector.Sample().MemoryUtilizationPercent.Status);
    }

    private sealed class Reader : IBasicMetricReader
    {
        internal uint[] Ticks = [10, 10, 10, 10];
        internal ulong Used = 25, Total = 100;
        internal bool FailCpu;
        public uint[] ReadCpuTicks() => FailCpu ? throw new IOException("failure") : Ticks;
        public (ulong Used, ulong Total) ReadMemory() => (Used, Total);
    }
}
