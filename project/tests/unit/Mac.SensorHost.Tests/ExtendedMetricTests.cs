using System.Buffers.Binary;
using System.Diagnostics;
using DesktopSystemMonitor.Core.Platform;
using Xunit;

namespace DesktopSystemMonitor.Mac.SensorHost.Tests;

public sealed class ExtendedMetricTests
{
    private static readonly DateTimeOffset Epoch = DateTimeOffset.UnixEpoch;
    private static long Tick(int second) => (second + 1L) * Stopwatch.Frequency;
    private static NetworkCounter Counter(uint received = 0, uint sent = 0) => new(1, "en0", 1, 1_000_000_000, received, sent, Is32Bit: true);

    [Fact]
    public void Network_Warms_Up_Calculates_Rates_And_Excludes_Virtual_And_Down_Interfaces()
    {
        NetworkCounter[] data = [Counter(), Counter() with { Index = 2, Name = "utun0" }, Counter() with { Index = 3, Flags = 0 }];
        var collector = new NetworkMetricCollector(() => data);
        Assert.Equal(SensorHostMetricStatus.WarmingUp, Assert.Single(collector.Sample(Tick(0), Epoch).Values).ReceiveBytesPerSecond.Status);
        data[0] = Counter(1024, 512);
        var next = Assert.Single(collector.Sample(Tick(1), Epoch.AddSeconds(1)).Values);
        Assert.Equal(1024, next.ReceiveBytesPerSecond.Value);
        Assert.Equal(512, next.SendBytesPerSecond.Value);
        data = [];
        Assert.Empty(collector.Sample(Tick(2), Epoch.AddSeconds(2)).Values);
        data = [Counter(2048)];
        Assert.Equal(SensorHostMetricStatus.WarmingUp, Assert.Single(collector.Sample(Tick(3), Epoch.AddSeconds(3)).Values).ReceiveBytesPerSecond.Status);
    }

    [Theory]
    [InlineData(4294967040U, 256U, false)]
    [InlineData(10000U, 0U, true)]
    [InlineData(0U, 3000000000U, true)]
    public void Network_Handles_Wrap_And_Rejects_Resets_Or_Implausible_Spikes(uint before, uint after, bool warmup)
    {
        var data = Counter(before);
        var collector = new NetworkMetricCollector(() => [data]);
        collector.Sample(Tick(0), Epoch);
        data = Counter(after);
        var value = Assert.Single(collector.Sample(Tick(1), Epoch.AddSeconds(1)).Values).ReceiveBytesPerSecond;
        Assert.Equal(warmup ? SensorHostMetricStatus.WarmingUp : SensorHostMetricStatus.Ok, value.Status);
        if (!warmup) Assert.Equal(512, value.Value);
    }

    [Fact]
    public void Network_Rebaselines_After_Sleep_Identity_Change_And_Failure()
    {
        var data = Counter();
        bool fail = false;
        var collector = new NetworkMetricCollector(() => fail ? throw new IOException("test") : [data]);
        collector.Sample(Tick(0), Epoch);
        data = Counter(10000);
        Assert.Equal(SensorHostMetricStatus.WarmingUp, collector.Sample(Tick(1), Epoch.AddSeconds(60)).Values[0].ReceiveBytesPerSecond.Status);
        data = data with { Name = "en1" };
        Assert.Equal(SensorHostMetricStatus.WarmingUp, collector.Sample(Tick(2), Epoch.AddSeconds(61)).Values[0].ReceiveBytesPerSecond.Status);
        fail = true;
        Assert.Equal(SensorHostMetricStatus.Unavailable, collector.Sample(Tick(3), Epoch.AddSeconds(62)).Status);
        fail = false;
        Assert.Equal(SensorHostMetricStatus.WarmingUp, collector.Sample(Tick(4), Epoch.AddSeconds(63)).Values[0].ReceiveBytesPerSecond.Status);
        Assert.Equal(SensorHostMetricStatus.Unavailable, new NetworkMetricCollector(() => [data, data]).Sample(Tick(0), Epoch).Status);
    }

    [Theory]
    [InlineData(1_000_000_000UL)]
    [InlineData(10_000_000_000UL)]
    [InlineData(25_000_000_000UL)]
    public void Network_Uses_64Bit_Deltas_At_Native_Poll_Interval(ulong baud)
    {
        var data = Counter() with { Is32Bit = false, BaudRate = baud, Received = uint.MaxValue, Sent = uint.MaxValue };
        var collector = new NetworkMetricCollector(() => [data]);
        collector.Sample(Tick(0), Epoch);
        data = data with { Received = data.Received + baud / 8, Sent = data.Sent + baud / 16 };
        var values = Assert.Single(collector.Sample(Tick(1), Epoch.AddSeconds(1)).Values);
        Assert.Equal(baud / 8d, values.ReceiveBytesPerSecond.Value);
        Assert.Equal(baud / 16d, values.SendBytesPerSecond.Value);
        data = data with { Received = 128, Sent = 128 };
        Assert.Equal(SensorHostMetricStatus.WarmingUp, Assert.Single(collector.Sample(Tick(2), Epoch.AddSeconds(2)).Values).ReceiveBytesPerSecond.Status);
        data = data with { Received = ulong.MaxValue };
        Assert.Equal(SensorHostMetricStatus.WarmingUp, Assert.Single(collector.Sample(Tick(3), Epoch.AddSeconds(3)).Values).ReceiveBytesPerSecond.Status);
    }

    [Theory]
    [InlineData(0UL)]
    [InlineData(25_000_000_000UL)]
    public void Network_Explains_Ambiguous_32Bit_Counters(ulong baud)
    {
        var data = Counter() with { BaudRate = baud };
        var collector = new NetworkMetricCollector(() => [data]);
        collector.Sample(Tick(0), Epoch);
        data = data with { Received = 1024 };
        var value = Assert.Single(collector.Sample(Tick(1), Epoch.AddSeconds(1)).Values).ReceiveBytesPerSecond;
        Assert.Equal(SensorHostMetricStatus.Unavailable, value.Status);
        Assert.Equal(SensorHostMetricReason.CounterRange, value.Reason);
    }

    private static byte[] Buffer()
    {
        byte[] data = new byte[160];
        BinaryPrimitives.WriteUInt16LittleEndian(data, 160);
        data[2] = 5; data[3] = 0x12;
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(12), 1);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(8), 1);
        BinaryPrimitives.WriteUInt64LittleEndian(data.AsSpan(96), 4294967552);
        return data;
    }

    [Fact]
    public void Network_Parser_Bounds_Checks_Every_Message()
    {
        var data = Buffer();
        Assert.Equal(4294967552UL, Assert.Single(NativeNetworkReader.Parse(data, _ => "en0")).Received);
        for (int length = 1; length < 160; length++)
            Assert.Throws<InvalidDataException>(() => NativeNetworkReader.Parse(data.AsSpan(0, length), _ => "en0"));
        data[0] = 0; data[1] = 0;
        Assert.Throws<InvalidDataException>(() => NativeNetworkReader.Parse(data, _ => "en0"));
        data = Buffer(); data[2] = 0;
        Assert.Throws<InvalidDataException>(() => NativeNetworkReader.Parse(data, _ => "en0"));
        data = Buffer(); data[12] = 0;
        Assert.Throws<InvalidDataException>(() => NativeNetworkReader.Parse(data, _ => "en0"));
        data = Buffer(); data[3] = 1;
        Assert.Empty(NativeNetworkReader.Parse(data, _ => "en0"));
        data = Buffer(); data[0] = 100;
        Assert.Throws<InvalidDataException>(() => NativeNetworkReader.Parse(data, _ => "en0"));
    }

    [Fact]
    public void Gpu_Discards_First_Read_And_Reprimes_After_Gap_Or_Invalid_Value()
    {
        var reader = new FakeGpu();
        using var collector = new GpuMetricCollector(reader);
        Assert.Equal(SensorHostMetricStatus.WarmingUp, collector.Sample(Tick(0), Epoch).Status);
        reader.Value = 47;
        Assert.Equal(47, collector.Sample(Tick(1), Epoch.AddSeconds(1)).Value);
        Assert.Equal(SensorHostMetricStatus.WarmingUp, collector.Sample(Tick(10), Epoch.AddSeconds(10)).Status);
        reader.Value = double.NaN;
        Assert.Equal(SensorHostMetricStatus.Unavailable, collector.Sample(Tick(11), Epoch.AddSeconds(11)).Status);
        reader.Value = 0;
        Assert.Equal(SensorHostMetricStatus.WarmingUp, collector.Sample(Tick(12), Epoch.AddSeconds(12)).Status);
        reader.Fail = true;
        Assert.Equal(SensorHostMetricStatus.Unavailable, collector.Sample(Tick(13), Epoch.AddSeconds(13)).Status);
        collector.Dispose();
        Assert.True(reader.Disposed);
        Assert.Equal(3, reader.Resets);
    }

    [MacFact]
    public async Task Native_Collectors_Read_Real_Network_And_Gpu_Without_Privileges()
    {
        Assert.NotEmpty(NativeNetworkReader.Read());
        using var collector = new NativeMetricCollector();
        var first = collector.Sample();
        Assert.Equal(SensorHostMetricStatus.WarmingUp, first.GpuUtilizationPercent.Status);
        await Task.Delay(1000);
        var second = collector.Sample();
        Assert.Equal(SensorHostMetricStatus.Ok, second.GpuUtilizationPercent.Status);
        Assert.InRange(second.GpuUtilizationPercent.Value!.Value, 0, 100);
        Assert.Equal(SensorHostMetricStatus.Ok, second.NetworkInterfaces.Status);
        Assert.NotEmpty(second.NetworkInterfaces.Values);
        Assert.All(second.NetworkInterfaces.Values, v => Assert.Equal(SensorHostMetricStatus.Ok, v.ReceiveBytesPerSecond.Status));
    }

    private sealed class FakeGpu : IGpuReader
    {
        internal double Value = 98;
        internal bool Fail, Disposed;
        internal int Resets;
        public double Read() => Fail ? throw new IOException("test") : Value;
        public void Reset() => Resets++;
        public void Dispose() => Disposed = true;
    }
}
