using System.Globalization;

namespace DesktopSystemMonitor.Core.Formatting;

public static class BytesFormatter
{
    public const string Unavailable = "N/A";

    public static string FormatBinaryMemory(long bytes)
    {
        if (bytes < 0)
        {
            return Unavailable;
        }
        double v = bytes;
        (double value, string unit) = v switch
        {
            >= 1L << 30 => (v / (1L << 30), "GiB"),
            >= 1L << 20 => (v / (1L << 20), "MiB"),
            >= 1L << 10 => (v / (1L << 10), "KiB"),
            _ => (v, "B"),
        };
        int decimals = value >= 100 ? 0 : value >= 10 ? 1 : 2;
        return value.ToString($"F{decimals}", CultureInfo.InvariantCulture) + " " + unit;
    }

    public static string FormatMemoryPair(long usage, long limit)
    {
        if (usage < 0 || limit < 0)
        {
            return Unavailable;
        }
        return FormatBinaryMemory(usage) + " / " + FormatBinaryMemory(limit);
    }

    public static string FormatCompactMemoryPair(long usage, long limit)
    {
        if (usage < 0 || limit <= 0)
        {
            return Unavailable;
        }

        (double divisor, string unit) = limit switch
        {
            >= 1L << 30 => (1L << 30, "GiB"),
            >= 1L << 20 => (1L << 20, "MiB"),
            >= 1L << 10 => (1L << 10, "KiB"),
            _ => (1d, "B"),
        };
        double limitValue = limit / divisor;
        double usageValue = usage / divisor;
        int decimals = limitValue >= 100 ? 0 : limitValue >= 10 ? 1 : 2;
        string format = $"F{decimals}";
        return usageValue.ToString(format, CultureInfo.InvariantCulture) + "/" +
            limitValue.ToString(format, CultureInfo.InvariantCulture) + " " + unit;
    }
}
