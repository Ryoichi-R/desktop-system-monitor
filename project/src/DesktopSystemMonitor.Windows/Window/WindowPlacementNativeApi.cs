using System.Runtime.InteropServices;
using DesktopSystemMonitor.Core.Layout;

namespace DesktopSystemMonitor.Windows.Window;

/// <summary>
/// Reads and moves a WPF overlay using the physical desktop rectangle exposed
/// by the per-monitor-aware HWND. Position changes preserve Z-order and focus.
/// Size changes are separate operations so a position-only correction never
/// resizes the window.
/// </summary>
public static partial class WindowPlacementNativeApi
{
    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOMOVE = 0x0002;
    private const uint SWP_NOZORDER = 0x0004;
    private const uint SWP_NOACTIVATE = 0x0010;

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    public static bool TryGetBounds(IntPtr window, out Rect bounds)
    {
        if (window != IntPtr.Zero && NativeGetWindowRect(window, out RECT rect))
        {
            bounds = new Rect(rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top);
            return bounds.Width > 0 && bounds.Height > 0;
        }

        bounds = default;
        return false;
    }

    public static bool TrySetPosition(IntPtr window, double left, double top)
    {
        if (window == IntPtr.Zero
            || !double.IsFinite(left)
            || !double.IsFinite(top)
            || left < int.MinValue
            || left > int.MaxValue
            || top < int.MinValue
            || top > int.MaxValue)
        {
            return false;
        }

        return NativeSetWindowPos(
            window,
            IntPtr.Zero,
            (int)Math.Round(left, MidpointRounding.AwayFromZero),
            (int)Math.Round(top, MidpointRounding.AwayFromZero),
            0,
            0,
            SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE);
    }

    /// <summary>
    /// Resizes the HWND to an exact physical pixel size while keeping its
    /// top-left corner, Z-order, and activation state.
    /// </summary>
    public static bool TrySetSize(IntPtr window, int width, int height)
    {
        if (window == IntPtr.Zero || width <= 0 || height <= 0)
        {
            return false;
        }

        return NativeSetWindowPos(
            window,
            IntPtr.Zero,
            0,
            0,
            width,
            height,
            SWP_NOMOVE | SWP_NOZORDER | SWP_NOACTIVATE);
    }

    /// <summary>
    /// Moves and resizes the HWND to an exact physical rectangle while keeping
    /// its Z-order and activation state.
    /// </summary>
    public static bool TrySetBounds(IntPtr window, Rect bounds)
    {
        if (window == IntPtr.Zero
            || !TryToInt(bounds.Left, out int left)
            || !TryToInt(bounds.Top, out int top)
            || !TryToInt(bounds.Width, out int width)
            || !TryToInt(bounds.Height, out int height)
            || width <= 0
            || height <= 0)
        {
            return false;
        }

        return NativeSetWindowPos(
            window,
            IntPtr.Zero,
            left,
            top,
            width,
            height,
            SWP_NOZORDER | SWP_NOACTIVATE);
    }

    private static bool TryToInt(double value, out int result)
    {
        double rounded = double.IsFinite(value) ? Math.Round(value, MidpointRounding.AwayFromZero) : double.NaN;
        if (!double.IsFinite(rounded) || rounded < int.MinValue || rounded > int.MaxValue)
        {
            result = 0;
            return false;
        }

        result = (int)rounded;
        return true;
    }

    public static uint GetDpiForWindow(IntPtr window) =>
        window == IntPtr.Zero ? 96u : NativeGetDpiForWindow(window);

    [LibraryImport("user32.dll", EntryPoint = "GetWindowRect")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool NativeGetWindowRect(IntPtr window, out RECT rect);

    [LibraryImport("user32.dll", EntryPoint = "SetWindowPos")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool NativeSetWindowPos(
        IntPtr window,
        IntPtr insertAfter,
        int x,
        int y,
        int width,
        int height,
        uint flags);

    [LibraryImport("user32.dll", EntryPoint = "GetDpiForWindow")]
    private static partial uint NativeGetDpiForWindow(IntPtr window);
}
