using DesktopSystemMonitor.Core.Metrics;
using DesktopSystemMonitor.Windows.Disk;
using Xunit;

namespace DesktopSystemMonitor.IntegrationTests;

[Trait("Category", "WindowsIntegration")]
public sealed class PdhDiskTests
{
    [SkippableFact]
    public async Task automatic_system_disk_matches_an_available_counter_and_returns_finite_values()
    {
        IReadOnlyList<int> available = DiskMetricSource.EnumerateAvailableDiskNumbers();
        Skip.If(available.Count == 0, "No PhysicalDisk performance counter is available.");

        using var source = new DiskMetricSource();
        source.SetEnabled(true, null);
        DiskSnapshot snapshot = await source.SampleAsync(default);
        Assert.Equal(MetricStatus.WarmingUp, snapshot.Status);

        DateTimeOffset deadline = DateTimeOffset.UtcNow.AddSeconds(3);
        do
        {
            await Task.Delay(250);
            snapshot = await source.SampleAsync(default);
        }
        while (snapshot.Status == MetricStatus.WarmingUp && DateTimeOffset.UtcNow < deadline);

        Assert.Equal(MetricStatus.Ok, snapshot.Status);
        Assert.NotNull(snapshot.SelectedDiskNumber);
        Assert.Contains(snapshot.SelectedDiskNumber.Value, available);
        Assert.True(double.IsFinite(snapshot.ActivePercent));
        Assert.True(double.IsFinite(snapshot.ReadBytesPerSecond));
        Assert.True(double.IsFinite(snapshot.WriteBytesPerSecond));
    }
}
