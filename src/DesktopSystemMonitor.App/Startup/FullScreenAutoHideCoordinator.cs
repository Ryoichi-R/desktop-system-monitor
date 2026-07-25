using DesktopSystemMonitor.Core.Settings;

namespace DesktopSystemMonitor.App.Startup;

internal enum FullScreenVisibilityTransition
{
    None,
    EnterHidden,
    ExitHidden,
}

internal sealed class FullScreenAutoHideCoordinator
{
    internal bool Hidden { get; private set; }

    internal FullScreenVisibilityTransition Evaluate(AppSettings settings, bool foregroundIsFullScreen)
    {
        bool shouldHide = settings.LayerMode == WindowLayerMode.AlwaysOnTop
            && settings.AutoHideOnFullScreen
            && foregroundIsFullScreen;
        if (shouldHide == Hidden)
        {
            return FullScreenVisibilityTransition.None;
        }
        Hidden = shouldHide;
        return shouldHide
            ? FullScreenVisibilityTransition.EnterHidden
            : FullScreenVisibilityTransition.ExitHidden;
    }
}
