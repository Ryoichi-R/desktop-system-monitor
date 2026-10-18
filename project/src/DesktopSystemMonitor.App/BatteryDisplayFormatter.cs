using System.Globalization;
using System.Windows;
using System.Windows.Media;
using DesktopSystemMonitor.Core.Formatting;
using DesktopSystemMonitor.Core.Settings;

namespace DesktopSystemMonitor.App;

internal static class BatteryDisplayFormatter
{
    internal const double AvailableSecondaryWidthDip = 242;
    internal const double SecondaryFontSize = 12;

    internal static string ChargingSecondary(
        double percent,
        int targetPercent,
        double? chargeMilliwatts,
        string fontFamilyName)
    {
        string current = double.IsFinite(percent)
            ? Math.Round(percent, MidpointRounding.AwayFromZero).ToString(CultureInfo.InvariantCulture) + "%"
            : "N/A";
        string target = targetPercent.ToString(CultureInfo.InvariantCulture) + "%";
        if (chargeMilliwatts is null || !double.IsFinite(chargeMilliwatts.Value) || chargeMilliwatts <= 0)
        {
            return $"{current}→{target}";
        }

        string power = PowerFormatter.FormatWatts(chargeMilliwatts.Value / 1000d);
        string compactPower = power.Replace(" ", string.Empty, StringComparison.Ordinal);
        string[] candidates =
        [
            $"{current} → {target} · {power}",
            $"{current}→{target} · {compactPower}",
            $"{current}→{target}",
        ];
        return candidates.First(candidate => Fits(candidate, fontFamilyName));
    }

    internal static string ChargingAtOrAboveTargetSecondary(double percent, int targetPercent)
    {
        string current = double.IsFinite(percent)
            ? Math.Round(percent, MidpointRounding.AwayFromZero).ToString(CultureInfo.InvariantCulture) + "%"
            : "N/A";
        return $"{current} · 目標{targetPercent.ToString(CultureInfo.InvariantCulture)}%";
    }

    internal static string ReachedSecondary(
        double percent,
        BatteryChargeTargetSource source)
    {
        string current = double.IsFinite(percent)
            ? Math.Round(percent, MidpointRounding.AwayFromZero).ToString(CultureInfo.InvariantCulture) + "%"
            : "N/A";
        string sourceLabel = source switch
        {
            BatteryChargeTargetSource.Manual => "手動",
            BatteryChargeTargetSource.Learned => "自動",
            _ => "未学習",
        };
        return $"{current} · {sourceLabel}";
    }

    internal static bool Fits(string value, string fontFamilyName)
    {
        Typeface typeface;
        try
        {
            typeface = new Typeface(string.IsNullOrWhiteSpace(fontFamilyName)
                ? "Segoe UI Variable Text"
                : fontFamilyName.Trim());
        }
        catch (ArgumentException)
        {
            typeface = new Typeface("Segoe UI Variable Text");
        }

        var formatted = new FormattedText(
            value,
            CultureInfo.CurrentUICulture,
            System.Windows.FlowDirection.LeftToRight,
            typeface,
            SecondaryFontSize,
            System.Windows.Media.Brushes.Black,
            pixelsPerDip: 1d);
        return formatted.WidthIncludingTrailingWhitespace <= AvailableSecondaryWidthDip;
    }
}
