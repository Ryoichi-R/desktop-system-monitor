using System.Windows.Threading;
using DesktopSystemMonitor.App.Startup;
using Application = System.Windows.Application;
using StartupEventArgs = System.Windows.StartupEventArgs;
using ExitEventArgs = System.Windows.ExitEventArgs;

namespace DesktopSystemMonitor.App;

public partial class App : Application
{
    private AppComposition? _composition;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += (_, args) =>
        {
            // Never crash the widget on a paint-time or dispatcher exception —
            // log and keep running.
            _composition?.RecordUnhandled(args.Exception);
            args.Handled = true;
        };

        _composition = AppComposition.Create();
        if (!_composition.TryClaimSingleInstance())
        {
            AppComposition.SignalExistingInstance();
            Shutdown();
            return;
        }
        _composition.StartMainWindow();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _composition?.Dispose();
        base.OnExit(e);
    }
}
