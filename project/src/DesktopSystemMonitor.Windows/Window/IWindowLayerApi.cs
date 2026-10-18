using System.Runtime.InteropServices;

namespace DesktopSystemMonitor.Windows.Window;

internal interface IWindowLayerApi
{
    WindowStyleObservation GetExtendedStyle(IntPtr hWnd);

    WindowPositionCallResult SetWindowPosition(IntPtr hWnd, IntPtr hWndInsertAfter, uint flags);
}

internal readonly record struct WindowStyleObservation(
    bool Succeeded,
    bool IsTopMost,
    int ErrorCode);

internal readonly record struct WindowPositionCallResult(
    bool Succeeded,
    int ErrorCode);

internal delegate IntPtr GetWindowLongPtrDelegate(IntPtr hWnd, int index);

internal delegate bool SetWindowPositionDelegate(
    IntPtr hWnd,
    IntPtr hWndInsertAfter,
    int x,
    int y,
    int width,
    int height,
    uint flags);

internal sealed class NativeWindowLayerApi : IWindowLayerApi
{
    private readonly GetWindowLongPtrDelegate _getWindowLongPtr;
    private readonly SetWindowPositionDelegate _setWindowPosition;

    public static NativeWindowLayerApi Instance { get; } = new();

    private NativeWindowLayerApi()
        : this(WindowInterop.GetWindowLongPtr, WindowInterop.SetWindowPos)
    {
    }

    internal NativeWindowLayerApi(
        GetWindowLongPtrDelegate getWindowLongPtr,
        SetWindowPositionDelegate setWindowPosition)
    {
        _getWindowLongPtr = getWindowLongPtr ?? throw new ArgumentNullException(nameof(getWindowLongPtr));
        _setWindowPosition = setWindowPosition ?? throw new ArgumentNullException(nameof(setWindowPosition));
    }

    public WindowStyleObservation GetExtendedStyle(IntPtr hWnd)
    {
        Marshal.SetLastPInvokeError(0);
        IntPtr value = _getWindowLongPtr(hWnd, WindowInterop.GWL_EXSTYLE);
        int errorCode = Marshal.GetLastPInvokeError();
        if (value == IntPtr.Zero && errorCode != 0)
        {
            return new WindowStyleObservation(false, false, errorCode);
        }

        bool isTopMost = (value.ToInt64() & WindowInterop.WS_EX_TOPMOST) != 0;
        return new WindowStyleObservation(true, isTopMost, 0);
    }

    public WindowPositionCallResult SetWindowPosition(IntPtr hWnd, IntPtr hWndInsertAfter, uint flags)
    {
        Marshal.SetLastPInvokeError(0);
        bool succeeded = _setWindowPosition(hWnd, hWndInsertAfter, 0, 0, 0, 0, flags);
        int errorCode = Marshal.GetLastPInvokeError();
        return new WindowPositionCallResult(succeeded, succeeded ? 0 : errorCode);
    }
}
