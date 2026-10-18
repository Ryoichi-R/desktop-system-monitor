using DesktopSystemMonitor.Core.Settings;

namespace DesktopSystemMonitor.Core.Layout;

public static class WidgetWidthCalculator
{
    public const double StandardWidgetWidthDip = 280d;
    public const double ReducedWidgetWidthDip = 150d;
    public const double WidgetPaddingDip = 8d;
    public const double ReducedContentWidthDip = 134d;

    public static double GetInformationWidth(WidgetDisplayMode displayMode) => displayMode switch
    {
        WidgetDisplayMode.Standard => StandardWidgetWidthDip,
        WidgetDisplayMode.Reduced => ReducedWidgetWidthDip,
        _ => StandardWidgetWidthDip,
    };

    public static double GetContentWidth(WidgetDisplayMode displayMode) =>
        GetInformationWidth(displayMode) - (WidgetPaddingDip * 2d);
}
