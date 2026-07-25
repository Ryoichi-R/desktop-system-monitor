namespace DesktopSystemMonitor.Windows.Window;

using System.Runtime.InteropServices;

public enum LayerStrategy
{
    /// <summary>Widget stays visually pinned behind normal windows.</summary>
    BottomMost,
    /// <summary>Widget floats above every other window.</summary>
    TopMost,
    /// <summary>Widget lives in the normal Z-order (safe fallback).</summary>
    Normal,
}

public static class BottomMostStrategy
{
    public static int TaskbarCreatedMessage { get; } =
        unchecked((int)WindowInterop.RegisterWindowMessage("TaskbarCreated"));

    /// <summary>
    /// Places <paramref name="hWnd"/> at the requested Z-order slot. This is
    /// idempotent so the caller can re-apply it from <c>WM_WINDOWPOSCHANGING</c>,
    /// activate, taskbar-recreated, and Win+D handlers without racing.
    /// </summary>
    public static bool Apply(IntPtr hWnd, LayerStrategy strategy)
    {
        if (hWnd == IntPtr.Zero)
        {
            return false;
        }
        try
        {
            return WindowLayerOperation.Apply(NativeWindowLayerApi.Instance, hWnd, strategy).Succeeded;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Rewrites an in-flight WINDOWPOS instead of recursively calling
    /// SetWindowPos from WM_WINDOWPOSCHANGING.
    /// </summary>
    public static bool PinWindowPosToBottom(IntPtr windowPosPointer)
    {
        try
        {
            return RewriteWindowPosForLayer(
                windowPosPointer,
                LayerStrategy.BottomMost,
                suppressRewrite: false,
                NativeWindowLayerApi.Instance);
        }
        catch
        {
            return false;
        }
    }

    internal static bool RewriteWindowPosForLayer(
        IntPtr windowPosPointer,
        LayerStrategy strategy,
        bool suppressRewrite,
        IWindowLayerApi api)
    {
        if (windowPosPointer == IntPtr.Zero || suppressRewrite || strategy == LayerStrategy.Normal)
        {
            return false;
        }

        WindowInterop.WINDOWPOS pos = Marshal.PtrToStructure<WindowInterop.WINDOWPOS>(windowPosPointer);
        if ((pos.flags & WindowInterop.SWP_NOZORDER) != 0)
        {
            return false;
        }

        IntPtr replacement;
        if (strategy == LayerStrategy.BottomMost)
        {
            replacement = WindowInterop.HWND_BOTTOM;
        }
        else
        {
            WindowStyleObservation? observation = IsSpecialInsertAfter(pos.hwndInsertAfter)
                ? null
                : api.GetExtendedStyle(pos.hwndInsertAfter);
            if (!ShouldRewriteTopMost(pos.hwndInsertAfter, observation))
            {
                return false;
            }
            replacement = WindowInterop.HWND_TOPMOST;
        }

        if (pos.hwndInsertAfter == replacement)
        {
            return false;
        }

        pos.hwndInsertAfter = replacement;
        Marshal.StructureToPtr(pos, windowPosPointer, fDeleteOld: false);
        return true;
    }

    internal static bool ShouldRewriteTopMost(
        IntPtr insertAfter,
        WindowStyleObservation? observation)
    {
        if (insertAfter == WindowInterop.HWND_NOTOPMOST || insertAfter == WindowInterop.HWND_BOTTOM)
        {
            return true;
        }
        if (insertAfter == WindowInterop.HWND_TOPMOST || insertAfter == WindowInterop.HWND_TOP)
        {
            return false;
        }

        return observation is { Succeeded: true, IsTopMost: false };
    }

    private static bool IsSpecialInsertAfter(IntPtr insertAfter) =>
        insertAfter == WindowInterop.HWND_NOTOPMOST
        || insertAfter == WindowInterop.HWND_BOTTOM
        || insertAfter == WindowInterop.HWND_TOPMOST
        || insertAfter == WindowInterop.HWND_TOP;
}
