using Avalonia;

namespace DesktopSystemMonitor.App;

internal static class Program
{
    internal static MacDiagnosticLog Diagnostics { get; set; } = new(new DesktopSystemMonitor.Mac.MacAppPathProvider().LogDirectory);

    [STAThread]
    public static int Main(string[] args)
    {
        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
        try { return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args); }
        catch (Exception ex)
        {
            Diagnostics.Record(MacDiagnosticEvent.UnhandledException, ex);
            return 1;
        }
        finally
        {
            Diagnostics.Record(MacDiagnosticEvent.Stopped);
            AppDomain.CurrentDomain.UnhandledException -= OnUnhandledException;
        }
    }

    private static void OnUnhandledException(object sender, UnhandledExceptionEventArgs args) =>
        Diagnostics.Record(MacDiagnosticEvent.UnhandledException, args.ExceptionObject as Exception);

    internal static AppBuilder BuildAvaloniaApp() => AvaloniaApp.BuildAvaloniaApp();
}
