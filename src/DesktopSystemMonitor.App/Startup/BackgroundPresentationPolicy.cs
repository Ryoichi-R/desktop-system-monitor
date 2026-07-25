using DesktopSystemMonitor.Core.Settings;

namespace DesktopSystemMonitor.App.Startup;

internal enum BackgroundPresentationMode
{
    None,
    Inline,
    Split,
}

internal static class BackgroundPresentationPolicy
{
    internal static BackgroundPresentationMode Evaluate(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (!settings.BackgroundEnabled || settings.EffectiveBackgroundOpacity <= 0)
        {
            return BackgroundPresentationMode.None;
        }

        return settings.HideBackgroundBehindWindows
            && settings.LayerMode == WindowLayerMode.AlwaysOnTop
                ? BackgroundPresentationMode.Split
                : BackgroundPresentationMode.Inline;
    }
}
