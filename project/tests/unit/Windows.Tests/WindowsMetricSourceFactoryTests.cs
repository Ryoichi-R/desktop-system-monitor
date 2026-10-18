using DesktopSystemMonitor.Windows;
using DesktopSystemMonitor.Windows.Battery;
using DesktopSystemMonitor.Windows.Cpu;
using DesktopSystemMonitor.Windows.Disk;
using DesktopSystemMonitor.Windows.Gpu;
using DesktopSystemMonitor.Windows.Memory;
using DesktopSystemMonitor.Windows.Network;
using DesktopSystemMonitor.Windows.Power;
using Xunit;

namespace DesktopSystemMonitor.Windows.Tests;

public sealed class WindowsMetricSourceFactoryTests
{
    [Fact]
    public void CreateCpuReturnsTheWindowsImplementation()
    {
        var factory = new WindowsMetricSourceFactory();

        using var source = factory.CreateCpu();

        Assert.IsType<CpuMetricSource>(source);
    }

    [Fact]
    public void CreateMemoryReturnsTheWindowsImplementation()
    {
        var factory = new WindowsMetricSourceFactory();

        using var source = factory.CreateMemory();

        Assert.IsType<MemoryMetricSource>(source);
    }

    [Fact]
    public void CreateGpuReturnsTheWindowsImplementation()
    {
        var factory = new WindowsMetricSourceFactory();

        using var source = factory.CreateGpu();

        Assert.IsType<GpuMetricSource>(source);
    }

    [Fact]
    public void CreateNetworkReturnsTheWindowsImplementation()
    {
        var factory = new WindowsMetricSourceFactory();

        using var source = factory.CreateNetwork();

        Assert.IsType<NetworkMetricSource>(source);
    }

    [Fact]
    public void CreateDiskReturnsTheWindowsImplementation()
    {
        var factory = new WindowsMetricSourceFactory();

        using var source = factory.CreateDisk();

        Assert.IsType<DiskMetricSource>(source);
    }

    [Fact]
    public void CreateBatteryReturnsTheWindowsImplementation()
    {
        var factory = new WindowsMetricSourceFactory();

        using var source = factory.CreateBattery();

        Assert.IsType<BatteryMetricSource>(source);
    }

    [Fact]
    public void CreatePowerReturnsTheWindowsImplementationAndForwardsDiagnostics()
    {
        var factory = new WindowsMetricSourceFactory();
        var received = new List<(string Stage, Exception Exception)>();

        using var source = factory.CreatePower((stage, exception) => received.Add((stage, exception)));

        Assert.IsType<HardwarePowerMetricSource>(source);
    }
}
