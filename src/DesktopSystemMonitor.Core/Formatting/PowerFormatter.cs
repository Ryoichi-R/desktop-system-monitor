using System.Globalization;

namespace DesktopSystemMonitor.Core.Formatting;

public static class PowerFormatter
{
    public const string WarmingUp = "--";
    public const string Unavailable = "N/A";

    public static string FormatWatts(double watts)
    {
        if (!double.IsFinite(watts) || watts < 0)
        {
            return Unavailable;
        }

        return watts.ToString("0.0", CultureInfo.InvariantCulture) + " W";
    }
}
