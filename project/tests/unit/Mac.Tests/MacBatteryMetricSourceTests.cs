using System.Threading.Tasks;

using DesktopSystemMonitor.Core.Metrics;
using DesktopSystemMonitor.Mac;

using Xunit;

namespace DesktopSystemMonitor.Mac.Tests;

public sealed class MacBatteryMetricSourceTests
{
    [Fact]
    public async Task Mac_Studio_Battery_Is_Always_Absent()
    {
        using var source = new MacBatteryMetricSource();

        BatterySnapshot snapshot = await source.SampleAsync(CancellationToken.None);

        Assert.Equal(BatteryPowerState.Absent, snapshot.PowerState);
        Assert.False(snapshot.BatteryPresent);
        Assert.Equal(MetricStatus.Unavailable, snapshot.Status);
    }
}
