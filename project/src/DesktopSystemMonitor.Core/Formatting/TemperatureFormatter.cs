using System.Globalization;

namespace DesktopSystemMonitor.Core.Formatting;

public static class TemperatureFormatter
{
    public const string WarmingUp = "--";
    public const string Unavailable = "N/A";

    public static string FormatCelsius(double value)
    {
        if (!double.IsFinite(value) || value is < -20 or > 150)
        {
            return Unavailable;
        }
        return Math.Round(value, MidpointRounding.AwayFromZero).ToString("F0", CultureInfo.InvariantCulture) + "°C";
    }
}
