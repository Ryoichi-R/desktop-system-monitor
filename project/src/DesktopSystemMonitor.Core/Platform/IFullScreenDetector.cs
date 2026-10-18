namespace DesktopSystemMonitor.Core.Platform;

/// <summary>
/// 自ウィンドウ以外の前面ウィンドウが全画面表示中かどうかを判定する抽象。
/// Windows実装はGetForegroundWindow等のuser32 API、macOS実装はNSWorkspace /
/// CGWindowListCopyWindowInfoでの実現をPhase 0で検討する。
/// </summary>
public interface IFullScreenDetector
{
    bool IsForegroundFullScreen(IntPtr ownWindow);
}
