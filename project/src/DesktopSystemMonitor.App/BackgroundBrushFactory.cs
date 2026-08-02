using DesktopSystemMonitor.Core.Settings;
using WpfBorder = System.Windows.Controls.Border;
using WpfFrameworkElement = System.Windows.FrameworkElement;
using MediaBrushes = System.Windows.Media.Brushes;
using MediaColor = System.Windows.Media.Color;
using MediaColorConverter = System.Windows.Media.ColorConverter;
using MediaGradientStop = System.Windows.Media.GradientStop;
using MediaLinearGradientBrush = System.Windows.Media.LinearGradientBrush;
using MediaSolidColorBrush = System.Windows.Media.SolidColorBrush;
using WpfPoint = System.Windows.Point;

namespace DesktopSystemMonitor.App;

internal static class BackgroundBrushFactory
{
    internal static void Apply(
        WpfFrameworkElement horizontalFadeHost,
        WpfBorder backgroundSurface,
        AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(horizontalFadeHost);
        ArgumentNullException.ThrowIfNull(backgroundSurface);
        ArgumentNullException.ThrowIfNull(settings);

        backgroundSurface.Background = CreateFill(settings);
        if (settings.BackgroundFillMode != BackgroundFillMode.EdgeFade)
        {
            horizontalFadeHost.OpacityMask = null;
            backgroundSurface.OpacityMask = null;
            return;
        }

        double expansionRatio = EdgeFadeExpansionRatio(settings);
        double maskRatio = expansionRatio / (1d + (2d * expansionRatio));
        horizontalFadeHost.OpacityMask = CreateHorizontalMask(maskRatio);
        backgroundSurface.OpacityMask = CreateVerticalMask(maskRatio);
    }

    internal static double EdgeFadeExpansionRatio(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return settings.BackgroundFillMode == BackgroundFillMode.EdgeFade
            ? Math.Clamp(settings.BackgroundEdgeFadePercent / 100d, 0.05, 0.5)
            : 0;
    }

    internal static void Clear(WpfFrameworkElement horizontalFadeHost, WpfBorder backgroundSurface)
    {
        ArgumentNullException.ThrowIfNull(horizontalFadeHost);
        ArgumentNullException.ThrowIfNull(backgroundSurface);

        horizontalFadeHost.OpacityMask = null;
        backgroundSurface.OpacityMask = null;
        backgroundSurface.Background = MediaBrushes.Transparent;
    }

    private static MediaSolidColorBrush CreateFill(AppSettings settings)
    {
        MediaColor rgb = (MediaColor)MediaColorConverter.ConvertFromString(settings.EffectiveBackgroundRgb);
        byte alpha = (byte)Math.Round(
            Math.Clamp(settings.EffectiveBackgroundOpacity, 0, 1) * byte.MaxValue,
            MidpointRounding.AwayFromZero);
        return Freeze(new MediaSolidColorBrush(MediaColor.FromArgb(alpha, rgb.R, rgb.G, rgb.B)));
    }

    private static MediaLinearGradientBrush CreateHorizontalMask(double fade)
    {
        var brush = new MediaLinearGradientBrush
        {
            StartPoint = new WpfPoint(0, 0.5),
            EndPoint = new WpfPoint(1, 0.5),
        };
        brush.GradientStops.Add(new MediaGradientStop(MediaColor.FromArgb(0, 255, 255, 255), 0));
        brush.GradientStops.Add(new MediaGradientStop(MediaColor.FromArgb(255, 255, 255, 255), fade));
        brush.GradientStops.Add(new MediaGradientStop(MediaColor.FromArgb(255, 255, 255, 255), 1 - fade));
        brush.GradientStops.Add(new MediaGradientStop(MediaColor.FromArgb(0, 255, 255, 255), 1));
        return Freeze(brush);
    }

    private static MediaLinearGradientBrush CreateVerticalMask(double fade)
    {
        var brush = new MediaLinearGradientBrush
        {
            StartPoint = new WpfPoint(0.5, 0),
            EndPoint = new WpfPoint(0.5, 1),
        };
        brush.GradientStops.Add(new MediaGradientStop(MediaColor.FromArgb(0, 255, 255, 255), 0));
        brush.GradientStops.Add(new MediaGradientStop(MediaColor.FromArgb(255, 255, 255, 255), fade));
        brush.GradientStops.Add(new MediaGradientStop(MediaColor.FromArgb(255, 255, 255, 255), 1 - fade));
        brush.GradientStops.Add(new MediaGradientStop(MediaColor.FromArgb(0, 255, 255, 255), 1));
        return Freeze(brush);
    }

    private static T Freeze<T>(T brush) where T : System.Windows.Media.Brush
    {
        brush.Freeze();
        return brush;
    }
}
