using Microsoft.Win32.SafeHandles;

namespace DesktopSystemMonitor.Windows.Pdh;

internal sealed class SafePdhQueryHandle : SafeHandleZeroOrMinusOneIsInvalid
{
    internal SafePdhQueryHandle(IntPtr handle)
        : base(ownsHandle: true)
    {
        SetHandle(handle);
    }

    protected override bool ReleaseHandle() => PdhInterop.PdhCloseQuery(handle) == 0;
}
