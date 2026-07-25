namespace DesktopSystemMonitor.Windows.Window;

public static class ClickThroughHelper
{
    public static void SetClickThrough(IntPtr hWnd, bool clickThrough)
    {
        if (hWnd == IntPtr.Zero)
        {
            return;
        }
        long ex = WindowInterop.GetWindowLongPtr(hWnd, WindowInterop.GWL_EXSTYLE).ToInt64();
        long updated = ex | WindowInterop.WS_EX_LAYERED | WindowInterop.WS_EX_TOOLWINDOW | WindowInterop.WS_EX_NOACTIVATE;
        if (clickThrough)
        {
            updated |= WindowInterop.WS_EX_TRANSPARENT;
        }
        else
        {
            updated &= ~WindowInterop.WS_EX_TRANSPARENT;
        }
        _ = WindowInterop.SetWindowLongPtr(hWnd, WindowInterop.GWL_EXSTYLE, new IntPtr(updated));
    }
}
