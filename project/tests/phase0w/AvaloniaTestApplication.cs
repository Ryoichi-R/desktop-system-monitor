using Avalonia;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;

using DesktopSystemMonitor.Avalonia.PoC;

[assembly: AvaloniaTestApplication(typeof(DesktopSystemMonitor.Avalonia.PoC.Tests.TestAppBuilder))]

namespace DesktopSystemMonitor.Avalonia.PoC.Tests;

public sealed class TestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>()
        .UseHeadless(new AvaloniaHeadlessPlatformOptions());
}
