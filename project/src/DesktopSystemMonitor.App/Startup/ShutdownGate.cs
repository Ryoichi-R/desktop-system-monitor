namespace DesktopSystemMonitor.App.Startup;

/// <summary>
/// Serializes shutdown start with work submission so no new callback can be
/// queued after <see cref="BeginShutdown"/> returns.
/// </summary>
internal sealed class ShutdownGate
{
    private readonly object _gate = new();
    private bool _shutdownStarted;

    internal bool IsShutdownStarted
    {
        get
        {
            lock (_gate)
            {
                return _shutdownStarted;
            }
        }
    }

    internal bool TryRunBeforeShutdown(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        lock (_gate)
        {
            if (_shutdownStarted)
            {
                return false;
            }
            action();
            return true;
        }
    }

    internal void BeginShutdown()
    {
        lock (_gate)
        {
            _shutdownStarted = true;
        }
    }
}
