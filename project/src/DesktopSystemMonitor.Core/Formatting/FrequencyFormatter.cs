using System.Globalization;

namespace DesktopSystemMonitor.Core.Formatting;

public static class FrequencyFormatter
{
    public const string Unavailable = "N/A";
    public const string WarmingUp = "--";

    public static string FormatGhz(double mhz)
    {
        if (double.IsNaN(mhz) || double.IsInfinity(mhz) || mhz <= 0)
        {
            return WarmingUp;
        }
        return (mhz / 1000d).ToString("F2", CultureInfo.InvariantCulture) + " GHz";
    }

    public static string FormatMhz(double mhz)
    {
        if (double.IsNaN(mhz) || double.IsInfinity(mhz) || mhz <= 0)
        {
            return WarmingUp;
        }
        return mhz.ToString("F0", CultureInfo.InvariantCulture) + " MHz";
    }
}
