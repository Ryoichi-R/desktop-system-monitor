using DesktopSystemMonitor.Core.Metrics;
using DesktopSystemMonitor.Windows.Battery;
using DesktopSystemMonitor.Windows.Cpu;
using DesktopSystemMonitor.Windows.Disk;
using DesktopSystemMonitor.Windows.Gpu;
using DesktopSystemMonitor.Windows.Memory;
using DesktopSystemMonitor.Windows.Network;
using DesktopSystemMonitor.Windows.Power;

namespace DesktopSystemMonitor.Windows;

/// <summary>PDH / DXGI / LibreHardwareMonitorLib実装でIMetricSourceFactoryへ適合させる。</summary>
public sealed class WindowsMetricSourceFactory : IMetricSourceFactory
{
    public IMetricSource<CpuSnapshot> CreateCpu() => new CpuMetricSource();
    public IMetricSource<MemorySnapshot> CreateMemory() => new MemoryMetricSource();
    public IGpuMetricSource CreateGpu() => new GpuMetricSource();
    public INetworkMetricSource CreateNetwork() => new NetworkMetricSource();
    public IDiskMetricSource CreateDisk() => new DiskMetricSource();
    public IBatteryMetricSource CreateBattery() => new BatteryMetricSource();

    public IPowerMetricSource CreatePower(Action<string, Exception> onDiagnostic) =>
        new HardwarePowerMetricSource((stage, exception) => onDiagnostic(stage, exception));
}
