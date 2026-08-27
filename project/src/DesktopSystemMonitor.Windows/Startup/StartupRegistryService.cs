using Microsoft.Win32;
using DesktopSystemMonitor.Core.Platform;

namespace DesktopSystemMonitor.Windows.Startup;

/// <summary>
/// Registers the app under
/// <c>HKCU\Software\Microsoft\Windows\CurrentVersion\Run</c>. The check is
/// path-aware so an entry left behind from an older install location is not
/// treated as active, and it never rewrites a disabled entry — matching Task
/// Manager's Startup UX.
/// </summary>
public sealed class StartupRegistryService : IStartupRegistry
{
    public const string ValueName = "DesktopSystemMonitor";
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

    private readonly Func<RegistryKey?> _openWrite;
    private readonly Func<RegistryKey?> _openRead;

    public StartupRegistryService() : this(
        openWrite: () => Registry.CurrentUser.OpenSubKey(RunKey, writable: true)
                        ?? Registry.CurrentUser.CreateSubKey(RunKey, writable: true),
        openRead: () => Registry.CurrentUser.OpenSubKey(RunKey, writable: false))
    {
    }

    internal StartupRegistryService(Func<RegistryKey?> openWrite, Func<RegistryKey?> openRead)
    {
        _openWrite = openWrite;
        _openRead = openRead;
    }

    public bool IsEnabled(string executablePath)
    {
        ArgumentNullException.ThrowIfNull(executablePath);
        using RegistryKey? key = _openRead();
        if (key is null)
        {
            return false;
        }
        object? value = key.GetValue(ValueName);
        if (value is not string entry)
        {
            return false;
        }
        return string.Equals(Normalize(entry), Normalize(executablePath), StringComparison.OrdinalIgnoreCase);
    }

    public void Enable(string executablePath)
    {
        ArgumentNullException.ThrowIfNull(executablePath);
        using RegistryKey? key = _openWrite();
        if (key is null)
        {
            throw new InvalidOperationException("Could not open HKCU Run key.");
        }
        key.SetValue(ValueName, $"\"{executablePath}\"", RegistryValueKind.String);
    }

    public void Disable()
    {
        using RegistryKey? key = _openWrite();
        if (key is null)
        {
            return;
        }
        key.DeleteValue(ValueName, throwOnMissingValue: false);
    }

    private static string Normalize(string value) =>
        value.Trim().Trim('"').Replace('/', '\\');
}
