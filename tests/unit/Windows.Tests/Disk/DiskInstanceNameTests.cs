using DesktopSystemMonitor.Windows.Disk;
using Xunit;

namespace DesktopSystemMonitor.Windows.Tests.Disk;

public sealed class DiskInstanceNameTests
{
    [Theory]
    [InlineData(@"\PhysicalDisk(0 C:)\% Idle Time", 0, "0 C:")]
    [InlineData(@"\PhysicalDisk(12 Data)\Disk Read Bytes/sec", 12, "12 Data")]
    [InlineData(@"\\WORKSTATION\PhysicalDisk(7 E:)\% Idle Time", 7, "7 E:")]
    public void parses_physical_disk_paths(string path, int number, string name)
    {
        Assert.True(DiskInstanceName.TryParseCounterPath(path, out var parsed));
        Assert.Equal(number, parsed.DiskNumber);
        Assert.Equal(name, parsed.InstanceName);
    }

    [Fact]
    public void excludes_total() => Assert.False(DiskInstanceName.TryParseCounterPath(@"\PhysicalDisk(_Total)\% Idle Time", out _));
}
