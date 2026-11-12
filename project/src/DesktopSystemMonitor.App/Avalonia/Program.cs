using Avalonia;
using DesktopSystemMonitor.Mac;

namespace DesktopSystemMonitor.App;

internal static class Program
{
    internal static MacDiagnosticLog Diagnostics { get; set; } = new(new DesktopSystemMonitor.Mac.MacAppPathProvider().LogDirectory);

    [STAThread]
    public static int Main(string[] args)
    {
        if (args is ["--version-info"])
        {
            Console.WriteLine(BuildInformation.Current.CopyText);
            return 0;
        }
        if (!OperatingSystem.IsMacOS()) return RunApplication(args);
        try
        {
            using var guard = MacSingleInstanceGuard.TryAcquire();
            return guard is null ? 0 : RunApplication(args);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DllNotFoundException or EntryPointNotFoundException)
        {
            Console.Error.WriteLine("Desktop System Monitor: 単一起動の確認に失敗しました。");
            return 2;
        }
    }

    private static int RunApplication(string[] args)
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
