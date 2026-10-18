using DesktopSystemMonitor.Core.Platform;

namespace DesktopSystemMonitor.Windows.Window;

/// <summary>GetForegroundWindow等のuser32 APIでIFullScreenDetectorへ適合させる。</summary>
public sealed class WindowsFullScreenDetector : IFullScreenDetector
{
    public bool IsForegroundFullScreen(IntPtr ownWindow) =>
        FullScreenDetector.IsForegroundFullScreen(ownWindow);
}
