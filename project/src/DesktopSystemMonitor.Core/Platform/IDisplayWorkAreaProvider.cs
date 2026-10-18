using DesktopSystemMonitor.Core.Layout;

namespace DesktopSystemMonitor.Core.Platform;

/// <summary>
/// 複数ディスプレイの作業領域・スケール・回転追従を解決する抽象。
/// Windows実装は物理作業領域を列挙し、表示中ウィンドウのDPIをGetDpiForWindowで取得する。
/// macOS実装はNSScreen.screensのvisibleFrame / backingScaleFactorを使う。
/// </summary>
public interface IDisplayWorkAreaProvider
{
    (IReadOnlyList<MonitorInfo> All, MonitorInfo Primary) Enumerate();
}
