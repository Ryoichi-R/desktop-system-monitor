using DesktopSystemMonitor.Windows;
using Xunit;

namespace DesktopSystemMonitor.Windows.Tests;

public sealed class WindowsAppPathProviderTests
{
    [Fact]
    public void ResolvesEachPathUnderTheAppDataDirectoryName()
    {
        var provider = new WindowsAppPathProvider();

        Assert.EndsWith(Path.Combine("DesktopSystemMonitor", "settings.json"), provider.SettingsFilePath);
        Assert.EndsWith(Path.Combine("DesktopSystemMonitor", "logs"), provider.LogDirectory);
    }

    [Fact]
    public void InternalConstructorHonorsTheProvidedLocalApplicationDataRoot()
    {
        string root = Path.Combine(Path.GetTempPath(), "DesktopSystemMonitor.Tests", Guid.NewGuid().ToString("N"));

        var provider = new WindowsAppPathProvider(root);

        Assert.Equal(
            Path.Combine(root, "DesktopSystemMonitor", "settings.json"),
            provider.SettingsFilePath);
        Assert.Equal(
            Path.Combine(root, "DesktopSystemMonitor", "logs"),
            provider.LogDirectory);
    }
}
