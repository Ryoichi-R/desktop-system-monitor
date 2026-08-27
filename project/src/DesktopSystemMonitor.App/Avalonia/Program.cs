using Avalonia;

namespace DesktopSystemMonitor.App;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args) => BuildAvaloniaApp()
        .StartWithClassicDesktopLifetime(args);

    internal static AppBuilder BuildAvaloniaApp() => AvaloniaApp.BuildAvaloniaApp();
}
