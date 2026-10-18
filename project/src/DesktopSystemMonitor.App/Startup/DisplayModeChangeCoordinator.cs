using DesktopSystemMonitor.Core.Settings;

namespace DesktopSystemMonitor.App.Startup;

internal enum DisplayModeChangeResult
{
    Unchanged,
    EditingRejected,
    SaveFailed,
    Applied,
}

internal static class DisplayModeChangeCoordinator
{
    internal static DisplayModeChangeResult Apply(
        AppSettings current,
        WidgetDisplayMode requested,
        bool editingAllowed,
        Func<AppSettings, bool> persistSettings,
        Action<AppSettings> replaceInMemory,
        Action<AppSettings> applyRuntime,
        Action<WidgetDisplayMode> synchronizeTray)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(persistSettings);
        ArgumentNullException.ThrowIfNull(replaceInMemory);
        ArgumentNullException.ThrowIfNull(applyRuntime);
        ArgumentNullException.ThrowIfNull(synchronizeTray);

        if (!editingAllowed)
        {
            synchronizeTray(current.DisplayMode);
            return DisplayModeChangeResult.EditingRejected;
        }

        AppSettings candidate = (current with { DisplayMode = requested }).Normalized();
        if (candidate.DisplayMode == current.DisplayMode)
        {
            synchronizeTray(current.DisplayMode);
            return DisplayModeChangeResult.Unchanged;
        }

        if (!persistSettings(candidate))
        {
            synchronizeTray(current.DisplayMode);
            return DisplayModeChangeResult.SaveFailed;
        }

        replaceInMemory(candidate);
        applyRuntime(candidate);
        synchronizeTray(candidate.DisplayMode);
        return DisplayModeChangeResult.Applied;
    }
}
