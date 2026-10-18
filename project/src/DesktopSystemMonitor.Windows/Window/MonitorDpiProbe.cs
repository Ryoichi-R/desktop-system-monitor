using System.Runtime.InteropServices;
using DesktopSystemMonitor.Core.Layout;

namespace DesktopSystemMonitor.Windows.Window;

internal interface IMonitorDpiProbeApi
{
    IntPtr SetThreadContext(IntPtr context);
    IntPtr CreateHiddenWindow(int x, int y);
    IntPtr GetWindowMonitor(IntPtr window);
    uint GetWindowDpi(IntPtr window);
    void DestroyWindow(IntPtr window);
}

/// <summary>
/// Measures each monitor's effective DPI with GetDpiForWindow on a temporary,
/// hidden per-monitor-v2 HWND. GetDpiForMonitor is not supported on a
/// per-monitor-aware calling thread; scale-factor enums can lose custom DPI.
/// No window is shown or activated, and the calling DPI context is restored.
/// These measurements are diagnostic data, not a replacement for the widget's
/// confirmed GetDpiForWindow value at the placement boundary.
/// </summary>
internal static class MonitorDpiProbe
{
    private static readonly IntPtr PerMonitorAwareV2 = new(-4);

    internal static double? ReadEffectiveDpi(Rect bounds, IntPtr monitor) =>
        ReadEffectiveDpi(bounds, monitor, new MonitorDpiProbeNativeApi());

    internal static double? ReadEffectiveDpi(Rect bounds, IntPtr monitor, IMonitorDpiProbeApi api)
    {
        ArgumentNullException.ThrowIfNull(api);
        double x = Math.Floor(bounds.Left + bounds.Width / 2d);
        double y = Math.Floor(bounds.Top + bounds.Height / 2d);
        if (monitor == IntPtr.Zero || !double.IsFinite(bounds.Width) || !double.IsFinite(bounds.Height)
            || bounds.Width <= 0 || bounds.Height <= 0
            || !double.IsFinite(x) || !double.IsFinite(y)
            || x < int.MinValue || x > int.MaxValue || y < int.MinValue || y > int.MaxValue)
        {
            return null;
        }

        IntPtr previous = api.SetThreadContext(PerMonitorAwareV2);
        if (previous == IntPtr.Zero)
        {
            return null;
        }
        IntPtr window = IntPtr.Zero;
        try
        {
            window = api.CreateHiddenWindow((int)x, (int)y);
            if (window == IntPtr.Zero || api.GetWindowMonitor(window) != monitor)
            {
                return null;
            }
            uint dpi = api.GetWindowDpi(window);
            return dpi > 0 ? dpi : null;
        }
        finally
        {
            try
            {
                if (window != IntPtr.Zero)
                {
                    api.DestroyWindow(window);
                }
            }
            finally
            {
                _ = api.SetThreadContext(previous);
            }
        }
    }
}

internal sealed partial class MonitorDpiProbeNativeApi : IMonitorDpiProbeApi
{
    // Deliberately omit WS_VISIBLE. STATIC is a predefined system class.
    private const uint WS_POPUP = 0x80000000;
    private const uint WS_EX_TOOLWINDOW = 0x00000080;
    private const uint WS_EX_NOACTIVATE = 0x08000000;

    public IntPtr SetThreadContext(IntPtr context) => NativeSetThreadDpiAwarenessContext(context);

    public IntPtr CreateHiddenWindow(int x, int y) => NativeCreateWindowEx(
        WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE, "STATIC", string.Empty, WS_POPUP,
        x, y, 1, 1, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);

    public uint GetWindowDpi(IntPtr window) => WindowPlacementNativeApi.GetDpiForWindow(window);

    public IntPtr GetWindowMonitor(IntPtr window) => NativeMonitorFromWindow(window, 0);

    public void DestroyWindow(IntPtr window) => _ = NativeDestroyWindow(window);

    [LibraryImport("user32.dll", EntryPoint = "SetThreadDpiAwarenessContext")]
    private static partial IntPtr NativeSetThreadDpiAwarenessContext(IntPtr context);

    [LibraryImport("user32.dll", EntryPoint = "MonitorFromWindow")]
    private static partial IntPtr NativeMonitorFromWindow(IntPtr window, uint flags);

    [LibraryImport("user32.dll", EntryPoint = "CreateWindowExW", StringMarshalling = StringMarshalling.Utf16)]
    private static partial IntPtr NativeCreateWindowEx(
        uint extendedStyle, string className, string windowName, uint style,
        int x, int y, int width, int height, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr parameter);

    [LibraryImport("user32.dll", EntryPoint = "DestroyWindow")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool NativeDestroyWindow(IntPtr window);
}
