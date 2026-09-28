using DesktopSystemMonitor.Mac;
using Xunit;

namespace DesktopSystemMonitor.Mac.Tests;

public sealed class MacStartupRegistryTests
{
    [Fact]
    public void Default_Constructor_Is_Usable_And_Relative_Homes_Are_Rejected()
    {
        var registry = new MacStartupRegistry();
        Assert.Throws<ArgumentException>(() => new MacStartupRegistry("relative-home"));
    }

    [Fact]
    public void Writes_Validated_LaunchAgent_And_Disables_It()
    {
        string root = Path.Combine(Path.GetTempPath(), "dsm-startup-" + Guid.NewGuid().ToString("N"));
        string executable = Path.Combine(root, "DesktopSystemMonitor.app", "Contents", "MacOS", "DesktopSystemMonitor");
        Directory.CreateDirectory(Path.GetDirectoryName(executable)!);
        File.WriteAllText(executable, "test");
        var registry = new MacStartupRegistry(root);
        try
        {
            registry.Enable(executable);
            Assert.True(registry.IsEnabled(executable));
            string plist = File.ReadAllText(registry.PlistPath);
            Assert.Contains("/dev/null", plist, StringComparison.Ordinal);
            Assert.DoesNotContain("StandardOutPath</key><string>" + executable, plist, StringComparison.Ordinal);

            registry.Disable();
            Assert.False(File.Exists(registry.PlistPath));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Rejects_Relative_And_Missing_Executables_And_Hides_Invalid_Plists()
    {
        string root = Path.Combine(Path.GetTempPath(), "dsm-startup-" + Guid.NewGuid().ToString("N"));
        var registry = new MacStartupRegistry(root);
        try
        {
            Assert.Throws<ArgumentException>(() => registry.Enable("relative-app"));
            Assert.False(registry.IsEnabled("relative-app"));
            Assert.Throws<FileNotFoundException>(() => registry.Enable(Path.Combine(root, "missing-app")));
            Directory.CreateDirectory(Path.GetDirectoryName(registry.PlistPath)!);
            File.WriteAllText(registry.PlistPath, "not xml");
            Assert.False(registry.IsEnabled(Path.Combine(root, "missing-app")));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Enable_And_Disable_Are_Idempotent_And_Only_Change_Next_Login()
    {
        string root = Path.Combine(Path.GetTempPath(), "dsm-startup-" + Guid.NewGuid().ToString("N"));
        string executable = Path.Combine(root, "DesktopSystemMonitor");
        Directory.CreateDirectory(root);
        File.WriteAllText(executable, "test");
        var registry = new MacStartupRegistry(root);
        try
        {
            registry.Enable(executable);
            registry.Enable(executable);
            Assert.True(registry.IsEnabled(executable));
            registry.Disable();
            registry.Disable();
            Assert.False(registry.IsEnabled(executable));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }
}
