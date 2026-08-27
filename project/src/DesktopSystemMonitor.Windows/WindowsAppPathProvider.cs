using DesktopSystemMonitor.Core.Platform;

namespace DesktopSystemMonitor.Windows;

/// <summary>LocalApplicationData配下を用途別に解決するIAppPathProviderのWindows実装。</summary>
public sealed class WindowsAppPathProvider : IAppPathProvider
{
    private const string AppDataDirectoryName = "DesktopSystemMonitor";

    public WindowsAppPathProvider()
        : this(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData))
    {
    }

    internal WindowsAppPathProvider(string localApplicationData)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(localApplicationData);
        SettingsFilePath = Path.Combine(localApplicationData, AppDataDirectoryName, "settings.json");
        LogDirectory = Path.Combine(localApplicationData, AppDataDirectoryName, "logs");
    }

    public string SettingsFilePath { get; }
    public string LogDirectory { get; }
}
