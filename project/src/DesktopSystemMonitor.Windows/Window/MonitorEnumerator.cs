using System.Runtime.InteropServices;
using DesktopSystemMonitor.Core.Layout;

namespace DesktopSystemMonitor.Windows.Window;

/// <summary>
/// Enumerates physical monitor work areas. The active window DPI is supplied by
/// GetDpiForWindow at the WPF placement boundary; this avoids querying monitor
/// DPI from a per-monitor-aware thread with GetDpiForMonitor.
/// </summary>
public static partial class MonitorEnumerator
{
    private const int MONITORINFOF_PRIMARY = 1;

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private unsafe struct MONITORINFOEXW
    {
        public uint cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
        public fixed char szDevice[32];
    }

    private delegate int MonitorEnumProc(IntPtr hMonitor, IntPtr hdc, IntPtr lprc, IntPtr data);

    [LibraryImport("user32.dll")]
    private static partial int EnumDisplayMonitors(IntPtr hdc, IntPtr lprcClip, MonitorEnumProc lpfnEnum, IntPtr dwData);

    [LibraryImport("user32.dll", EntryPoint = "GetMonitorInfoW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFOEXW info);

    public static (IReadOnlyList<MonitorInfo> All, MonitorInfo Primary) Enumerate()
    {
        var monitors = new List<MonitorInfo>();
        MonitorInfo? primary = null;

        unsafe
        {
            EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (hMon, _, _, _) =>
            {
                var info = new MONITORINFOEXW { cbSize = (uint)sizeof(MONITORINFOEXW) };
                if (!GetMonitorInfo(hMon, ref info))
                {
                    return 1;
                }
                var physicalWork = new Rect(
                    Left: info.rcWork.Left,
                    Top: info.rcWork.Top,
                    Width: info.rcWork.Right - info.rcWork.Left,
                    Height: info.rcWork.Bottom - info.rcWork.Top);
                string device = new(info.szDevice);
                var m = new MonitorInfo(device, physicalWork, 96)
                {
                    PhysicalWorkArea = physicalWork,
                };
                monitors.Add(m);
                if ((info.dwFlags & MONITORINFOF_PRIMARY) != 0)
                {
                    primary = m;
                }
                return 1;
            }, IntPtr.Zero);
        }
        if (primary is null && monitors.Count > 0)
        {
            primary = monitors[0];
        }
        primary ??= new MonitorInfo("Primary", new Rect(0, 0, 1920, 1080), 96);
        return (monitors, primary);
    }
}
