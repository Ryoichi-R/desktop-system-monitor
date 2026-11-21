using Avalonia;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;

using DesktopSystemMonitor.App;
using DesktopSystemMonitor.Core.Platform;

[assembly: AvaloniaTestApplication(typeof(DesktopSystemMonitor.App.Avalonia.Tests.TestAppBuilder))]

namespace DesktopSystemMonitor.App.Avalonia.Tests;

public sealed class TestAppBuilder
{
    internal static AvaloniaApp CreateApplication()
    {
        var settings = new MemorySettingsStore();
        var startup = new MemoryStartupRegistry();
        return new AvaloniaApp
        {
            SettingsStoreFactory = () => settings,
            StartupRegistryFactory = () => startup,
        };
    }

    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure(CreateApplication)
        .UseHeadless(new AvaloniaHeadlessPlatformOptions());
}

internal sealed class MemoryStartupRegistry : IStartupRegistry
{
    internal bool Enabled { get; set; }
    public bool IsEnabled(string executablePath) => Enabled;
    public void Enable(string executablePath) => Enabled = true;
    public void Disable() => Enabled = false;
}
