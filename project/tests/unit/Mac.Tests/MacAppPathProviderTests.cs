using Xunit;

namespace DesktopSystemMonitor.Mac.Tests;

public sealed class MacAppPathProviderTests
{
    [Fact]
    public void Explicit_Settings_And_Log_Roots_Are_Separate()
    {
        var paths = new MacAppPathProvider(Path.GetFullPath("test-home"));
        Assert.EndsWith(Path.Combine("Library", "Application Support", "DesktopSystemMonitor", "mac-widget.json"), paths.SettingsFilePath);
        Assert.EndsWith(Path.Combine("Library", "Logs", "DesktopSystemMonitor"), paths.LogDirectory);
        Assert.True(Path.IsPathFullyQualified(new MacAppPathProvider().SettingsFilePath));
        Assert.Throws<ArgumentException>(() => new MacAppPathProvider("relative"));
    }
}
