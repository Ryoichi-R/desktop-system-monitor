namespace DesktopSystemMonitor.Windows.Window;

public static class FullScreenDetector
{
    public static bool IsForegroundFullScreen(IntPtr ownWindow)
    {
        return IsForegroundFullScreen(ownWindow, User32FullScreenWindowApi.Instance);
    }

    internal static bool IsForegroundFullScreen(IntPtr ownWindow, IFullScreenWindowApi api)
    {
        ArgumentNullException.ThrowIfNull(api);

        IntPtr foreground = api.GetForegroundWindow();
        if (foreground == IntPtr.Zero || foreground == ownWindow || api.IsIconic(foreground))
        {
            return false;
        }

        IntPtr shellWindow = api.GetShellWindow();
        if (shellWindow != IntPtr.Zero && foreground == shellWindow)
        {
            return false;
        }

        IntPtr desktopWindow = api.GetDesktopWindow();
        if (desktopWindow != IntPtr.Zero && foreground == desktopWindow)
        {
            return false;
        }

        if (IsExplorerDesktopHost(foreground, shellWindow, api))
        {
            return false;
        }

        if (!api.TryGetWindowBounds(foreground, out ScreenBounds windowBounds)
            || !api.TryGetMonitorBounds(foreground, out ScreenBounds monitorBounds)
            || !api.TryGetMonitorBounds(ownWindow, out ScreenBounds ownMonitorBounds)
            || monitorBounds != ownMonitorBounds)
        {
            return false;
        }

        const int tolerance = 1;
        return windowBounds.Left <= monitorBounds.Left + tolerance
            && windowBounds.Top <= monitorBounds.Top + tolerance
            && windowBounds.Right >= monitorBounds.Right - tolerance
            && windowBounds.Bottom >= monitorBounds.Bottom - tolerance;
    }

    private static bool IsExplorerDesktopHost(
        IntPtr foreground,
        IntPtr shellWindow,
        IFullScreenWindowApi api)
    {
        if (shellWindow == IntPtr.Zero
            || !api.TryGetProcessId(shellWindow, out uint shellProcessId)
            || !api.TryGetProcessId(foreground, out uint foregroundProcessId)
            || shellProcessId == 0
            || foregroundProcessId == 0
            || shellProcessId != foregroundProcessId
            || !api.TryGetClassName(foreground, out string className))
        {
            return false;
        }

        return className.Equals("Progman", StringComparison.Ordinal)
            || className.Equals("WorkerW", StringComparison.Ordinal);
    }
}
