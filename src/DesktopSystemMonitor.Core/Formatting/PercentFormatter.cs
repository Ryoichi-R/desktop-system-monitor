using System.Globalization;

namespace DesktopSystemMonitor.Core.Formatting;

public static class PercentFormatter
{
    public const string Unavailable = "N/A";
    public const string WarmingUp = "--";

    public static string Format(double percent, int decimals = 0)
    {
        (string value, string unit) = FormatParts(percent, decimals);
        return value + unit;
    }

    public static (string Value, string Unit) FormatParts(double percent, int decimals = 0)
    {
        if (!double.IsFinite(percent))
        {
            return (WarmingUp, string.Empty);
        }
        double clamped = Math.Clamp(percent, 0d, 100d);
        return (clamped.ToString($"F{decimals}", CultureInfo.InvariantCulture), "%");
    }

    public static double ToFraction(double percent)
    {
        if (!double.IsFinite(percent))
        {
            return 0d;
        }
        return Math.Clamp(percent, 0d, 100d) / 100d;
    }
}
