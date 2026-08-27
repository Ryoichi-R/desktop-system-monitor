namespace DesktopSystemMonitor.Core.Platform;

/// <summary>
/// OSログイン時の自動起動設定。Windows実装はレジストリRunキー、
/// macOS実装は~/Library/LaunchAgents/*.plist（またはSMAppService）を使う。
/// </summary>
public interface IStartupRegistry
{
    bool IsEnabled(string executablePath);
    void Enable(string executablePath);
    void Disable();
}
