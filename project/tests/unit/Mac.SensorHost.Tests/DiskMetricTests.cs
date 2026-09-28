using System.Diagnostics;
using DesktopSystemMonitor.Core.Platform;
using Xunit;

namespace DesktopSystemMonitor.Mac.SensorHost.Tests;

public sealed class DiskMetricTests
{
    private static long Tick(int second) => (second + 1L) * Stopwatch.Frequency;
    private static readonly DateTimeOffset Epoch = DateTimeOffset.UnixEpoch;

    [Fact]
    public void Aggregates_Devices_And_Rebaselines_After_Reset_Reconnect_And_Sleep()
    {
        DiskCounter[] data = [new(1, 100, 200), new(2, 50, 80)];
        var collector = new DiskMetricCollector(() => data);
        SensorHostMetrics Sample(int second, int? wall = null) => collector.Sample(SensorHostMetrics.Empty, Tick(second), Epoch.AddSeconds(wall ?? second));
        Assert.Equal(SensorHostMetricStatus.WarmingUp, Sample(0).DiskReadBytesPerSecond.Status);
        data = [new(2, 80, 120), new(1, 200, 400)];
        var metrics = Sample(1);
        Assert.Equal(130, metrics.DiskReadBytesPerSecond.Value);
        Assert.Equal(240, metrics.DiskWriteBytesPerSecond.Value);
        // A per-device reset must not be hidden by increases on another device.
        data = [new(1, 0, 0), new(2, 1000, 1000)];
        Assert.Equal(SensorHostMetricStatus.WarmingUp, Sample(2).DiskReadBytesPerSecond.Status);
        data = [new(1, 10, 10), new(3, 1000, 1000)];
        Assert.Equal(SensorHostMetricStatus.WarmingUp, Sample(3).DiskReadBytesPerSecond.Status);
        Assert.Equal(SensorHostMetricStatus.WarmingUp, Sample(10).DiskReadBytesPerSecond.Status);
        Assert.Equal(SensorHostMetricStatus.WarmingUp, Sample(11, 0).DiskReadBytesPerSecond.Status);
        Assert.Equal(SensorHostMetricStatus.WarmingUp, Sample(10, 1).DiskReadBytesPerSecond.Status);
        data = [new(1, 10, 10)];
        Assert.Equal(SensorHostMetricStatus.WarmingUp, Sample(12, 2).DiskReadBytesPerSecond.Status);
        Assert.Equal(0, Sample(13, 3).DiskReadBytesPerSecond.Value);
        Assert.Equal(SensorHostMetricStatus.Unavailable, metrics.DiskUtilizationPercent.Status);
    }

    [Fact]
    public void Failure_Invalid_Data_Overflow_And_Empty_Do_Not_Publish_Partial_Or_Stale_Data()
    {
        DiskCounter[] data = [new(1, 1, 1)];
        bool fail = false;
        var collector = new DiskMetricCollector(() => fail ? throw new IOException("test") : data);
        var original = SensorHostMetrics.Empty with { CpuUtilizationPercent = SensorHostMetricValue.Ok(12, Tick(0)), DiskReadBytesPerSecond = SensorHostMetricValue.Ok(999, Tick(0)) };
        fail = true;
        var failed = collector.Sample(original, Tick(0), Epoch);
        Assert.Equal(12, failed.CpuUtilizationPercent.Value);
        Assert.Null(failed.DiskReadBytesPerSecond.Value);
        fail = false;
        Assert.Equal(SensorHostMetricStatus.WarmingUp, collector.Sample(original, Tick(1), Epoch.AddSeconds(1)).DiskReadBytesPerSecond.Status);
        foreach (var invalid in new DiskCounter[][] { [], [new(0, 1, 1)], [new(1, -1, 1)], [new(1, 1, -1)], [new(1, 1, 1), new(1, 1, 1)], [new(1, long.MaxValue, 1), new(2, 1, 1)], Enumerable.Range(1, 65).Select(x => new DiskCounter((ulong)x, 1, 1)).ToArray() })
        {
            data = invalid;
            Assert.Equal(SensorHostMetricStatus.Unavailable, collector.Sample(original, Tick(2), Epoch.AddSeconds(2)).DiskReadBytesPerSecond.Status);
        }
    }

    [Fact]
    public void Native_Reader_On_Mac_Produces_Bounded_Valid_Counters()
    {
        if (!OperatingSystem.IsMacOS()) return;
        var data = NativeDiskReader.Read();
        Assert.InRange(data.Length, 1, 64);
        Assert.All(data, x => { Assert.NotEqual(0UL, x.Identity); Assert.True(x.ReadBytes >= 0); Assert.True(x.WrittenBytes >= 0); });
    }
}
