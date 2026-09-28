using System.Buffers.Binary;
using System.Diagnostics;
using DesktopSystemMonitor.Core.Platform;
using Xunit;

namespace DesktopSystemMonitor.Mac.SensorHost.Tests;

public sealed class ProcessMetricTests
{
    private static long Tick(int second) => (second + 1L) * Stopwatch.Frequency;
    private static readonly DateTimeOffset Epoch = DateTimeOffset.UnixEpoch;
    private static ProcessCounter Counter(int pid = 1, ulong cpu = 0) => new(pid, 1, "test", cpu, 0, 0);

    [Fact]
    public void Normalizes_Cpu_To_Total_Capacity_And_Handles_Pid_Reuse_Reset_And_Exit()
    {
        ProcessCounter[] data = [Counter()];
        var collector = new ProcessMetricCollector(() => new(data, 1e-9, 4));
        SensorHostMetricGroup<SensorHostProcessReading> Sample(int second, int? wall = null) => collector.Sample(Tick(second), Epoch.AddSeconds(wall ?? second));
        Assert.Equal(SensorHostMetricStatus.WarmingUp, Sample(0).Status);
        data = [Counter(cpu: 1_000_000_000) with { ReadBytes = 100, WrittenBytes = 200 }];
        var value = Assert.Single(Sample(1).Values);
        Assert.Equal(25, value.CpuUtilizationPercent.Value);
        Assert.Equal(100, value.IoReadBytesPerSecond.Value);
        Assert.Equal(200, value.IoWriteBytesPerSecond.Value);
        Assert.Equal(SensorHostMetricStatus.Unavailable, value.PrivateBytes.Status);
        data = [Counter(cpu: 2_000_000_000) with { Start = 2 }];
        Assert.Empty(Sample(2).Values);
        data = [Counter(cpu: 3_000_000_000) with { Start = 2, Name = "new" }];
        Assert.Empty(Sample(3).Values);
        data = [Counter(cpu: 1) with { Start = 2, Name = "new" }];
        Assert.Empty(Sample(4).Values);
        data = [];
        Assert.Empty(Sample(5).Values);
        data = [Counter()];
        Assert.Empty(Sample(6).Values);
        Assert.Equal(SensorHostMetricStatus.WarmingUp, Sample(10).Status);
        Assert.Equal(SensorHostMetricStatus.WarmingUp, Sample(11, 0).Status);
        Assert.Equal(SensorHostMetricStatus.WarmingUp, Sample(10, 1).Status);
    }

    [Fact]
    public void Top_Ten_Are_Bounded_And_Failure_Clears_The_Baseline()
    {
        var data = Enumerable.Range(1, 40).Select(pid => Counter(pid)).ToArray();
        bool fail = false;
        var collector = new ProcessMetricCollector(() => fail ? throw new IOException("test") : new(data, 1e-9, 4));
        collector.Sample(Tick(0), Epoch);
        data = data.Select(x => x with { CpuTicks = (ulong)x.Pid * 1_000_000 }).ToArray();
        var rows = collector.Sample(Tick(1), Epoch.AddSeconds(1)).Values;
        Assert.Equal(10, rows.Length);
        Assert.Equal(40, rows[0].ProcessId);
        Assert.Equal(31, rows[9].ProcessId);
        fail = true;
        Assert.Equal(SensorHostMetricStatus.Unavailable, collector.Sample(Tick(2), Epoch.AddSeconds(2)).Status);
        fail = false;
        Assert.Equal(SensorHostMetricStatus.WarmingUp, collector.Sample(Tick(3), Epoch.AddSeconds(3)).Status);
        data = [Counter(), Counter()];
        Assert.Equal(SensorHostMetricStatus.Unavailable, collector.Sample(Tick(4), Epoch.AddSeconds(4)).Status);
        Assert.Equal(SensorHostMetricStatus.Unavailable, new ProcessMetricCollector(() => new([], double.NaN, 0)).Sample(Tick(0), Epoch).Status);
    }

    [Fact]
    public void Parser_Validates_Size_Identity_Counters_And_Name()
    {
        byte[] data = new byte[160];
        BinaryPrimitives.WriteUInt64LittleEndian(data.AsSpan(80), 10);
        BinaryPrimitives.WriteUInt64LittleEndian(data.AsSpan(16), 20);
        BinaryPrimitives.WriteUInt64LittleEndian(data.AsSpan(24), 30);
        Assert.Equal(50UL, NativeProcessReader.Parse(1, "test", data)!.CpuTicks);
        Assert.Throws<InvalidDataException>(() => NativeProcessReader.Parse(1, "test", data.AsSpan(0, 159)));
        Assert.Null(NativeProcessReader.Parse(0, "test", data));
        Assert.Null(NativeProcessReader.Parse(1, "bad\nname", data));
        BinaryPrimitives.WriteUInt64LittleEndian(data.AsSpan(144), ulong.MaxValue);
        Assert.Null(NativeProcessReader.Parse(1, "test", data));
    }

    [Fact]
    public void Native_Reader_Cpu_Delta_Matches_Managed_Process_Cpu_Time()
    {
        if (!OperatingSystem.IsMacOS()) return;
        using var process = Process.GetCurrentProcess();
        var before = NativeProcessReader.Read();
        var first = before.Values.Single(x => x.Pid == Environment.ProcessId);
        process.Refresh();
        double cpuBefore = process.TotalProcessorTime.TotalSeconds;
        var elapsed = Stopwatch.StartNew();
        while (elapsed.ElapsedMilliseconds < 150) Thread.SpinWait(1000);
        var after = NativeProcessReader.Read();
        var last = after.Values.Single(x => x.Pid == Environment.ProcessId);
        process.Refresh();
        double managed = process.TotalProcessorTime.TotalSeconds - cpuBefore;
        double native = (last.CpuTicks - first.CpuTicks) * after.SecondsPerCpuTick;
        Assert.InRange(native, 0.02, 2);
        Assert.InRange(Math.Abs(native - managed), 0, 0.08);
        Assert.Equal(first.Start, last.Start);
    }
}
