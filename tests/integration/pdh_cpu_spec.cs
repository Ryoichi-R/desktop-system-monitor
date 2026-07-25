using DesktopSystemMonitor.Core.Metrics;
using DesktopSystemMonitor.Windows.Cpu;
using DesktopSystemMonitor.Windows.Pdh;
using DesktopSystemMonitor.Windows.Power;
using Xunit;

namespace DesktopSystemMonitor.IntegrationTests;

[Trait("Category", "WindowsIntegration")]
public sealed class PdhCpuTests
{
    [Fact]
    public async Task english_cpu_counter_can_be_collected_and_disposed()
    {
        using var query = new PdhQuery();
        const string path = @"\Processor Information(_Total)\% Processor Time";
        Assert.True(query.TryAddCounter(path));
        Assert.True(query.Collect());
        await Task.Delay(250);
        Assert.True(query.Collect());
        Assert.True(query.TryGetDouble(path, out double value));
        Assert.True(double.IsFinite(value));
    }

    [Fact]
    public async Task cpu_source_warms_up_then_returns_a_non_crashing_snapshot()
    {
        using var source = new CpuMetricSource();
        CpuSnapshot first = await source.SampleAsync(default);
        Assert.Equal(MetricStatus.WarmingUp, first.UtilizationStatus);
        await Task.Delay(250);
        CpuSnapshot second = await source.SampleAsync(default);
        Assert.Contains(second.UtilizationStatus, new[] { MetricStatus.Ok, MetricStatus.Unavailable });
        if (second.UtilizationStatus == MetricStatus.Ok)
        {
            Assert.InRange(second.UtilizationPercent, 0, 100);
        }
    }

    [SkippableFact]
    public async Task energy_meter_cpu_power_is_positive_when_supported_counter_is_present()
    {
        IReadOnlyList<string> paths = PdhQuery.ExpandWildcard(
            EnergyMeterCpuPowerReader.PowerWildcardPath);
        SelectedCpuPowerPaths selected = EnergyMeterCpuPowerReader.SelectPowerPaths(paths);
        Skip.If(
            selected.Paths.Length == 0,
            "No supported Energy Meter CPU package or cluster power counter is available.");

        using ICpuPowerReader reader = EnergyMeterCpuPowerReader.TryCreate(onError: null);

        CpuPowerReading reading = CpuPowerReading.Unavailable();
        DateTimeOffset deadline = DateTimeOffset.UtcNow.AddSeconds(3);
        do
        {
            reading = reader.Sample();
            if (reading.Status == MetricStatus.Ok)
            {
                break;
            }
            await Task.Delay(250);
        }
        while (DateTimeOffset.UtcNow < deadline);

        Assert.Equal(MetricStatus.Ok, reading.Status);
        Assert.InRange(reading.Watts, 0.01, PowerSensorSelector.MaximumReasonableWatts);
    }
}
