using DesktopSystemMonitor.Core.Platform;

namespace DesktopSystemMonitor.Mac;

public sealed class MacAppPathProvider : IAppPathProvider
{
    private readonly string _applicationSupportDirectory;
    private readonly string _logDirectory;

    public MacAppPathProvider()
        : this(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile))
    {
    }

    internal MacAppPathProvider(string userHome)
    {
        if (string.IsNullOrWhiteSpace(userHome) || !IsMacAbsolutePath(userHome))
        {
            throw new ArgumentException("A fully qualified user home path is required.", nameof(userHome));
        }

        _applicationSupportDirectory = JoinMacPath(
            userHome,
            "Library",
            "Application Support",
            "DesktopSystemMonitor");
        _logDirectory = JoinMacPath(userHome, "Library", "Logs", "DesktopSystemMonitor");
    }

    public string SettingsFilePath => JoinMacPath(_applicationSupportDirectory, "settings.json");

    public string LogDirectory => _logDirectory;

    private static bool IsMacAbsolutePath(string path) => path.StartsWith('/');

    private static string JoinMacPath(params string[] parts) =>
        string.Join("/", parts.Select(part => part.Trim('/'))).Insert(0, "/");
}
