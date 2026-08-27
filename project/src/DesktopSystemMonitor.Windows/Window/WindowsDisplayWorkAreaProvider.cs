using DesktopSystemMonitor.Core.Layout;
using DesktopSystemMonitor.Core.Platform;

namespace DesktopSystemMonitor.Windows.Window;

/// <summary>EnumDisplayMonitors + GetDpiForMonitorでIDisplayWorkAreaProviderへ適合させる。</summary>
public sealed class WindowsDisplayWorkAreaProvider : IDisplayWorkAreaProvider
{
    public (IReadOnlyList<MonitorInfo> All, MonitorInfo Primary) Enumerate() =>
        MonitorEnumerator.Enumerate();
}
