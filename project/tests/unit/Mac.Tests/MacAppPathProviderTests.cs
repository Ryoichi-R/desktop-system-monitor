using DesktopSystemMonitor.Mac;

using Xunit;

namespace DesktopSystemMonitor.Mac.Tests;

public sealed class MacAppPathProviderTests
{
    [Fact]
    public void Uses_Explicit_MacOS_Application_And_Log_Directories()
    {
        var provider = new MacAppPathProvider(@"/Users/tester");

        Assert.Equal(
            @"/Users/tester/Library/Application Support/DesktopSystemMonitor/settings.json",
            provider.SettingsFilePath);
        Assert.Equal(@"/Users/tester/Library/Logs/DesktopSystemMonitor", provider.LogDirectory);
    }

    [Fact]
    public void Rejects_NonAbsolute_User_Home()
    {
        Assert.Throws<ArgumentException>(() => new MacAppPathProvider("relative-home"));
    }
}
