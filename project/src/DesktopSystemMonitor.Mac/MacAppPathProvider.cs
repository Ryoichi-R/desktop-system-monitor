using DesktopSystemMonitor.Core.Platform;

namespace DesktopSystemMonitor.Mac;

public sealed class MacAppPathProvider : IAppPathProvider
{
    public MacAppPathProvider() : this(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)) { }
    internal MacAppPathProvider(string home)
    {
        if (string.IsNullOrWhiteSpace(home) || !Path.IsPathFullyQualified(home)) throw new ArgumentException("An absolute home directory is required.", nameof(home));
        SettingsFilePath = Path.Combine(home, "Library", "Application Support", "DesktopSystemMonitor", "mac-widget.json");
        LogDirectory = Path.Combine(home, "Library", "Logs", "DesktopSystemMonitor");
    }
    public string SettingsFilePath { get; }
    public string LogDirectory { get; }
}
