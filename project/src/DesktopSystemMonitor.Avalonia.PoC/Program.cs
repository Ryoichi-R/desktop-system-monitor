using Avalonia;

namespace DesktopSystemMonitor.Avalonia.PoC;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args) => BuildAvaloniaApp()
        .StartWithClassicDesktopLifetime(args);

    public static AppBuilder BuildAvaloniaApp() => App.BuildAvaloniaApp();
}
