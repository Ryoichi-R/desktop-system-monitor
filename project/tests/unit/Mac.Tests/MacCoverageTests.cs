using DesktopSystemMonitor.Core.Metrics;
using DesktopSystemMonitor.Mac;

using Xunit;

namespace DesktopSystemMonitor.Mac.Tests;

public sealed class MacCoverageTests
{
    [Fact]
    public void Default_Path_Provider_Uses_The_Current_User_Home_When_Mac_Path_Is_Available()
    {
        if (OperatingSystem.IsMacOS())
        {
            var provider = new MacAppPathProvider();
            Assert.StartsWith("/", provider.SettingsFilePath);
        }
        else
        {
            Assert.Throws<ArgumentException>(() => new MacAppPathProvider());
        }
    }

    [Fact]
    public async Task Battery_Source_NoOp_Controls_Are_Safe()
    {
        using var source = new MacBatteryMetricSource();

        source.ResetBaseline();
        source.SetEnabled(false);
        source.SetEnabled(true);
        BatterySnapshot snapshot = await source.SampleAsync(CancellationToken.None);

        Assert.Equal(BatteryPowerState.Absent, snapshot.PowerState);
    }

    [Fact]
    public async Task Factory_Sources_Expose_Safe_NoOp_Controls()
    {
        var factory = new MacMetricSourceFactory();
        using IMetricSource<CpuSnapshot> cpu = factory.CreateCpu();
        using IMetricSource<MemorySnapshot> memory = factory.CreateMemory();
        using IGpuMetricSource gpu = factory.CreateGpu();
        using INetworkMetricSource network = factory.CreateNetwork();
        using IPowerMetricSource power = factory.CreatePower((_, _) => { });
        using IDiskMetricSource disk = factory.CreateDisk();

        cpu.ResetBaseline();
        memory.ResetBaseline();
        gpu.ResetBaseline();
        gpu.SetPreferredAdapter(1);
        network.ResetBaseline();
        network.SetSelectedAdapters([1, 2]);
        power.ResetBaseline();
        power.PausePolling();
        power.ResumePolling();
        disk.ResetBaseline();
        disk.SetEnabled(true, 1);

        Assert.Equal(MetricStatus.Unavailable, (await cpu.SampleAsync(CancellationToken.None)).UtilizationStatus);
        Assert.Equal(MetricStatus.Unavailable, (await memory.SampleAsync(CancellationToken.None)).Status);
    }

    [Fact]
    public void Real_File_System_Uses_Canonical_Regular_File_Checks()
    {
        string path = Path.GetTempFileName();
        try
        {
            var fileSystem = new RealSensorHostFileSystem();

            Assert.Equal(Path.GetFullPath(path), fileSystem.GetCanonicalPath(path));
            Assert.True(fileSystem.IsRegularFile(path));
            Assert.False(fileSystem.IsRegularFile(Path.GetDirectoryName(path)!));
            Assert.False(fileSystem.IsSymbolicLink(path));
            Assert.Equal(Path.GetFullPath(path), MacRealPath.Resolve(Path.GetFullPath(path)));
            if (OperatingSystem.IsMacOS())
            {
                Assert.Throws<IOException>(() => MacRealPath.Resolve(path + ".missing"));
            }
        }
        finally
        {
            File.Delete(path);
        }
    }
}
