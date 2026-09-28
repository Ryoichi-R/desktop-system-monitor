using Xunit;

namespace DesktopSystemMonitor.Mac.SensorHost.Tests;

public sealed class MachBasicMetricReaderTests
{
    [MacFact]
    public void Public_Mach_Counters_Are_Read_Without_Privileges()
    {
        var reader = new MachBasicMetricReader();
        var ticks = reader.ReadCpuTicks();
        Assert.Equal(4, ticks.Length);
        Assert.Contains(ticks, value => value > 0);
        var memory = reader.ReadMemory();
        Assert.True(memory.Total > 0);
        Assert.InRange(memory.Used, 0UL, memory.Total);
    }
}

internal sealed class MacFactAttribute : FactAttribute
{
    public MacFactAttribute()
    {
        if (!OperatingSystem.IsMacOS()) Skip = "macOS public API integration test";
    }
}
