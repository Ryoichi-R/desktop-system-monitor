namespace DesktopSystemMonitor.Core.Platform;

/// <summary>
/// アプリが解決する全OS標準フォルダーを用途別に返す。
/// Environment.GetFolderPath(SpecialFolder.LocalApplicationData)等の暗黙マッピングに
/// 個別箇所が依存しないよう、設定・診断ログの基点をここへ集約する。
/// Windows実装はLocalApplicationData配下、macOS実装は明示的に
/// ~/Library/Application Support/DesktopSystemMonitor 等を返す。
/// </summary>
public interface IAppPathProvider
{
    /// <summary>設定ファイル（settings.json）の完全パス。</summary>
    string SettingsFilePath { get; }

    /// <summary>診断ログを置くディレクトリ。</summary>
    string LogDirectory { get; }
}
