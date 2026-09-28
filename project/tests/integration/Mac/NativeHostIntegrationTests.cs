using System.Diagnostics;
using DesktopSystemMonitor.Core.Platform;
using DesktopSystemMonitor.Mac;
using Xunit;

namespace DesktopSystemMonitor.IntegrationTests.Mac;

public sealed class NativeHostIntegrationTests
{
    [Fact]
    public async Task Real_Host_Roundtrips_Validated_Metrics_And_Recovers_After_Termination()
    {
        Assert.True(OperatingSystem.IsMacOS(), "Run the Mac integration suite on macOS.");
        string path = Path.Combine(AppContext.BaseDirectory, "host", "DesktopSystemMonitor.Mac.SensorHost");
        Assert.True(File.Exists(path));
        ProcessSensorHostConnection? connection = null;
        await using var session = new MacSensorSession(() => connection = new ProcessSensorHostConnection(path), Stopwatch.GetTimestamp, TimeSpan.FromSeconds(2));
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var first = await session.SampleAsync(deadline.Token);
        Assert.Equal(SensorHostMetricStatus.Ok, first.MemoryTotalBytes.Status);
        await Task.Delay(1100, deadline.Token);
        var second = await session.SampleAsync(deadline.Token);
        Assert.Equal(SensorHostMetricStatus.Ok, second.CpuUtilizationPercent.Status);
        Assert.Equal(SensorHostMetricStatus.Ok, second.DiskReadBytesPerSecond.Status);
        Assert.Equal(SensorHostMetricStatus.Ok, second.HighLoadProcesses.Status);
        Assert.InRange(second.HighLoadProcesses.Values.Length, 1, 10);
        connection!.Abort();
        var missing = await session.SampleAsync(deadline.Token);
        Assert.Equal(SensorHostMetricStatus.Unavailable, missing.MemoryTotalBytes.Status);
        Assert.Empty(missing.HighLoadProcesses.Values);
        await Task.Delay(1200, deadline.Token);
        var recovered = await session.SampleAsync(deadline.Token);
        Assert.Equal(SensorHostMetricStatus.Ok, recovered.MemoryTotalBytes.Status);
        Assert.Equal(SensorHostMetricStatus.WarmingUp, recovered.HighLoadProcesses.Status);
        await Task.Delay(1100, deadline.Token);
        Assert.Equal(SensorHostMetricStatus.Ok, (await session.SampleAsync(deadline.Token)).HighLoadProcesses.Status);
    }
}
