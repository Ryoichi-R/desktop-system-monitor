using Avalonia;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;

using DesktopSystemMonitor.App;

[assembly: AvaloniaTestApplication(typeof(DesktopSystemMonitor.App.Avalonia.Tests.TestAppBuilder))]

namespace DesktopSystemMonitor.App.Avalonia.Tests;

public sealed class TestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<AvaloniaApp>()
        .UseHeadless(new AvaloniaHeadlessPlatformOptions());
}
