using DesktopSystemMonitor.Windows.Startup;
using Microsoft.Win32;
using Xunit;

namespace DesktopSystemMonitor.Windows.Tests.Startup;

/// <summary>
/// Uses a scratch HKCU subkey created for the test process so we don't touch
/// the real Run key. Skipped on non-Windows CI runners.
/// </summary>
[Trait("Category", "WindowsUnit")]
public sealed class StartupRegistryServiceTests : IDisposable
{
    private readonly string _keyPath;

    public StartupRegistryServiceTests()
    {
        _keyPath = @"Software\DesktopSystemMonitor.Tests\Run-" + Guid.NewGuid().ToString("N");
        using var _ = Registry.CurrentUser.CreateSubKey(_keyPath, writable: true);
    }

    private StartupRegistryService NewService() => new(
        openWrite: () => Registry.CurrentUser.OpenSubKey(_keyPath, writable: true)
                        ?? Registry.CurrentUser.CreateSubKey(_keyPath, writable: true),
        openRead: () => Registry.CurrentUser.OpenSubKey(_keyPath, writable: false));

    [Fact]
    public void enable_then_disable_is_symmetrical()
    {
        var svc = NewService();
        string exe = @"C:\Programs\DSM\DesktopSystemMonitor.exe";
        svc.Enable(exe);
        Assert.True(svc.IsEnabled(exe));
        svc.Disable();
        Assert.False(svc.IsEnabled(exe));
    }

    [Fact]
    public void stale_entry_from_a_different_path_is_not_considered_enabled()
    {
        var svc = NewService();
        svc.Enable(@"C:\Old\DesktopSystemMonitor.exe");
        Assert.False(svc.IsEnabled(@"C:\New\DesktopSystemMonitor.exe"));
    }

    [Fact]
    public void path_prefix_is_not_mistaken_for_the_registered_executable()
    {
        var svc = NewService();
        svc.Enable(@"C:\Programs\DSM\DesktopSystemMonitor.exe.old");
        Assert.False(svc.IsEnabled(@"C:\Programs\DSM\DesktopSystemMonitor.exe"));
    }

    [Fact]
    public void enable_overwrites_previous_value()
    {
        var svc = NewService();
        svc.Enable(@"C:\First\DesktopSystemMonitor.exe");
        svc.Enable(@"C:\Second\DesktopSystemMonitor.exe");
        Assert.False(svc.IsEnabled(@"C:\First\DesktopSystemMonitor.exe"));
        Assert.True(svc.IsEnabled(@"C:\Second\DesktopSystemMonitor.exe"));
    }

    public void Dispose()
    {
        try
        {
            Registry.CurrentUser.DeleteSubKeyTree(_keyPath, throwOnMissingSubKey: false);
            Registry.CurrentUser.DeleteSubKey(@"Software\DesktopSystemMonitor.Tests", throwOnMissingSubKey: false);
        }
        catch
        {
            // best effort
        }
    }
}
