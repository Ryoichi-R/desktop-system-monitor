using System.Diagnostics;
using DesktopSystemMonitor.Core.Platform;
using Xunit;
namespace DesktopSystemMonitor.Mac.SensorHost.Tests;

public sealed class ThermalMetricTests
{
    private static long Tick(int n) => (n + 1L) * Stopwatch.Frequency;
    [Fact]
    public void Temperatures_Filter_Invalid_And_Partial_Data_And_Power_Smooths()
    {
        var temperature = new FakeTemperature();
        var energy = new FakeEnergy();
        using var collector = new ThermalMetricCollector(temperature, energy);
        var wall = DateTimeOffset.UnixEpoch;
        var first = collector.Sample(SensorHostMetrics.Empty, Tick(0), wall);
        Assert.Equal(50, first.CpuTemperatureCelsius.Value);
        Assert.Equal(50, first.GpuTemperatureCelsius.Value);
        Assert.Equal(SensorHostMetricStatus.WarmingUp, first.GpuPowerWatts.Status);
        energy.Joules = 10;
        Assert.Equal(SensorHostMetricStatus.WarmingUp, collector.Sample(first, Tick(1), wall.AddSeconds(1)).GpuPowerWatts.Status);
        energy.Joules = 20;
        Assert.Equal(15, collector.Sample(first, Tick(2), wall.AddSeconds(2)).GpuPowerWatts.Value);
        temperature.Missing = TemperatureProfile.Cpu.Take(3).ToHashSet();
        var partial = collector.Sample(first, Tick(3), wall.AddSeconds(3));
        Assert.Equal(SensorHostMetricStatus.Unavailable, partial.CpuTemperatureCelsius.Status);
        Assert.Equal(SensorHostMetricStatus.Ok, partial.GpuTemperatureCelsius.Status);
        temperature.Supported = false;
        energy.Fail = true;
        var failed = collector.Sample(first, Tick(4), wall.AddSeconds(4));
        Assert.Equal(SensorHostMetricStatus.Unavailable, failed.CpuTemperatureCelsius.Status);
        Assert.Equal(SensorHostMetricStatus.Unavailable, failed.GpuPowerWatts.Status);
        energy.Fail = false; energy.Joules = null;
        Assert.Equal(SensorHostMetricStatus.WarmingUp, collector.Sample(first, Tick(10), wall.AddSeconds(60)).GpuPowerWatts.Status);
        energy.Joules = double.NaN;
        Assert.Equal(SensorHostMetricStatus.Unavailable, collector.Sample(first, Tick(11), wall.AddSeconds(61)).GpuPowerWatts.Status);
    }

    [Fact]
    public void Native_Decoders_Validate_Types_Sizes_And_Units()
    {
        Assert.Equal(42.5, SmcTemperatureReader.Decode(SmcTemperatureReader.FourCc("flt "), BitConverter.GetBytes(42.5f)));
        Assert.Equal(42.5, SmcTemperatureReader.Decode(SmcTemperatureReader.FourCc("sp78"), new byte[] { 42, 128 }));
        Assert.Equal(42, SmcTemperatureReader.Decode(SmcTemperatureReader.FourCc("ioft"), BitConverter.GetBytes(42L * 65536)));
        Assert.Null(SmcTemperatureReader.Decode(SmcTemperatureReader.FourCc("flt "), new byte[2]));
        Assert.Throws<ArgumentException>(() => SmcTemperatureReader.FourCc("bad"));
        Assert.Equal(1, GpuEnergyReader.ToJoules(1000000000, "nJ"));
        Assert.Equal(1, GpuEnergyReader.ToJoules(1000000, "uJ"));
        Assert.Equal(1, GpuEnergyReader.ToJoules(1000, "mJ"));
        Assert.Equal(1, GpuEnergyReader.ToJoules(1, "J"));
        Assert.Throws<InvalidDataException>(() => GpuEnergyReader.ToJoules(-1, "nJ"));
        Assert.Throws<InvalidDataException>(() => GpuEnergyReader.ToJoules(1, "bad"));
    }

    [MacFact]
    public async Task Calibrated_Mac_Reads_Temperatures_And_Gpu_Power()
    {
        using var smc = new SmcTemperatureReader();
        if (!smc.Supported) return; // Other OS/model profiles intentionally remain unavailable.
        using var collector = new ThermalMetricCollector(smc, new GpuEnergyReader());
        SensorHostMetrics sample = SensorHostMetrics.Empty;
        for (int i = 0; i < 3; i++)
        {
            if (i > 0) await Task.Delay(1000);
            sample = collector.Sample(sample, Stopwatch.GetTimestamp(), DateTimeOffset.UtcNow);
        }
        Assert.Equal(SensorHostMetricStatus.Ok, sample.CpuTemperatureCelsius.Status);
        Assert.Equal(SensorHostMetricStatus.Ok, sample.GpuTemperatureCelsius.Status);
        Assert.Equal(SensorHostMetricStatus.Ok, sample.GpuPowerWatts.Status);
    }

    [MacFact]
    public async Task Native_Energy_Channel_Returns_Validated_Joules()
    {
        using var reader = new GpuEnergyReader();
        Assert.Null(reader.ReadJoules());
        await Task.Delay(1000);
        Assert.NotNull(reader.ReadJoules());
    }

    private sealed class FakeTemperature : ITemperatureReader
    {
        public bool Supported { get; set; } = true;
        internal HashSet<string> Missing = new();
        public double? Read(string key) => Missing.Contains(key) ? double.NaN : 50;
        public void Dispose() { }
    }
    private sealed class FakeEnergy : IGpuEnergyReader
    {
        internal double? Joules;
        internal bool Fail;
        public double? ReadJoules() => Fail ? throw new IOException("test") : Joules;
        public void Reset() { }
        public void Dispose() { }
    }
}
