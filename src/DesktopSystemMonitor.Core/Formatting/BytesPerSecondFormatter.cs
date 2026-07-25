using System.Globalization;

namespace DesktopSystemMonitor.Core.Formatting;

public enum RateUnitSystem
{
    /// <summary>Powers of 1000, byte-based (KB/s, MB/s).</summary>
    DecimalBytes,
    /// <summary>Powers of 1000, bit-based (Kbps, Mbps).</summary>
    DecimalBits,
    /// <summary>Decimal kilobits per second without automatic unit scaling.</summary>
    FixedKilobitsPerSecond,
}

public static class BytesPerSecondFormatter
{
    public const string WarmingUp = "--";
    public const string Unavailable = "N/A";

    public static string Format(double bytesPerSecond, RateUnitSystem system = RateUnitSystem.DecimalBytes)
    {
        if (double.IsNaN(bytesPerSecond) || double.IsInfinity(bytesPerSecond))
        {
            return WarmingUp;
        }
        if (bytesPerSecond < 0)
        {
            bytesPerSecond = 0;
        }

        return system switch
        {
            RateUnitSystem.DecimalBits => FormatBits(bytesPerSecond * 8d),
            RateUnitSystem.FixedKilobitsPerSecond => FormatFixedKilobits(bytesPerSecond),
            _ => FormatBytes(bytesPerSecond),
        };
    }

    private static string FormatFixedKilobits(double bytesPerSecond)
    {
        double value = bytesPerSecond * 8d / 1_000d;
        return FormatValue(value) + " Kb/s";
    }

    private static string FormatBytes(double bps)
    {
        (double value, string unit) = bps switch
        {
            >= 1_000_000_000d => (bps / 1_000_000_000d, "GB/s"),
            >= 1_000_000d => (bps / 1_000_000d, "MB/s"),
            >= 1_000d => (bps / 1_000d, "KB/s"),
            _ => (bps, "B/s"),
        };
        return FormatValue(value) + " " + unit;
    }

    private static string FormatBits(double bps)
    {
        (double value, string unit) = bps switch
        {
            >= 1_000_000_000d => (bps / 1_000_000_000d, "Gbps"),
            >= 1_000_000d => (bps / 1_000_000d, "Mbps"),
            >= 1_000d => (bps / 1_000d, "Kbps"),
            _ => (bps, "bps"),
        };
        return FormatValue(value) + " " + unit;
    }

    private static string FormatValue(double value)
    {
        int decimals = value >= 100 ? 0 : value >= 10 ? 1 : 2;
        return value.ToString($"F{decimals}", CultureInfo.InvariantCulture);
    }
}
