using System.Globalization;
using DesktopSystemMonitor.Core.Formatting;
using Xunit;

namespace DesktopSystemMonitor.Core.Tests.Formatting;

public class PercentFormatterTests
{
    [Theory]
    [InlineData(-0.1, "0%")]
    [InlineData(0.0, "0%")]
    [InlineData(42.4, "42%")]
    [InlineData(99.9, "100%")]
    [InlineData(120.0, "100%")]
    public void clamps_and_rounds(double input, string expected)
    {
        Assert.Equal(expected, PercentFormatter.Format(input));
    }

    [Fact]
    public void nan_renders_as_warming_up()
    {
        Assert.Equal(PercentFormatter.WarmingUp, PercentFormatter.Format(double.NaN));
    }

    [Fact]
    public void positive_infinity_renders_as_warming_up()
    {
        Assert.Equal(PercentFormatter.WarmingUp, PercentFormatter.Format(double.PositiveInfinity));
    }

    [Theory]
    [InlineData(45.0, 0, "45", "%")]
    [InlineData(-1.0, 0, "0", "%")]
    [InlineData(120.0, 0, "100", "%")]
    [InlineData(42.45, 1, "42.5", "%")]
    public void format_parts_separates_value_and_unit(double input, int decimals, string value, string unit)
    {
        Assert.Equal((value, unit), PercentFormatter.FormatParts(input, decimals));
    }

    [Fact]
    public void invalid_parts_have_no_unit()
    {
        Assert.Equal((PercentFormatter.WarmingUp, string.Empty), PercentFormatter.FormatParts(double.NaN));
        Assert.Equal((PercentFormatter.WarmingUp, string.Empty), PercentFormatter.FormatParts(double.PositiveInfinity));
    }

    [Theory]
    [InlineData(-10.0, 0.0)]
    [InlineData(0.0, 0.0)]
    [InlineData(50.0, 0.5)]
    [InlineData(100.0, 1.0)]
    [InlineData(120.0, 1.0)]
    [InlineData(double.NaN, 0.0)]
    [InlineData(double.PositiveInfinity, 0.0)]
    public void fraction_is_clamped(double input, double expected)
    {
        Assert.Equal(expected, PercentFormatter.ToFraction(input));
    }

    [Fact]
    public void japanese_culture_still_uses_invariant_period()
    {
        var previous = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = new CultureInfo("ja-JP");
        try
        {
            Assert.Equal("42.5%", PercentFormatter.Format(42.5, decimals: 1));
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }
}

public class FrequencyFormatterTests
{
    [Fact]
    public void ghz_from_mhz()
    {
        Assert.Equal("3.20 GHz", FrequencyFormatter.FormatGhz(3200));
    }

    [Fact]
    public void non_positive_or_nan_is_warmup()
    {
        Assert.Equal(FrequencyFormatter.WarmingUp, FrequencyFormatter.FormatGhz(0));
        Assert.Equal(FrequencyFormatter.WarmingUp, FrequencyFormatter.FormatGhz(-100));
        Assert.Equal(FrequencyFormatter.WarmingUp, FrequencyFormatter.FormatGhz(double.NaN));
        Assert.Equal(FrequencyFormatter.WarmingUp, FrequencyFormatter.FormatGhz(double.PositiveInfinity));
    }

    [Theory]
    [InlineData(3200, "3200 MHz")]
    [InlineData(999.6, "1000 MHz")]
    public void mhz_is_invariant_and_rounded(double input, string expected)
    {
        Assert.Equal(expected, FrequencyFormatter.FormatMhz(input));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void invalid_mhz_is_warmup(double input)
    {
        Assert.Equal(FrequencyFormatter.WarmingUp, FrequencyFormatter.FormatMhz(input));
    }
}

public class PowerFormatterTests
{
    [Theory]
    [InlineData(0, "0.0 W")]
    [InlineData(42.34, "42.3 W")]
    [InlineData(125.96, "126.0 W")]
    public void watts_use_one_invariant_decimal(double input, string expected)
    {
        Assert.Equal(expected, PowerFormatter.FormatWatts(input));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void invalid_watts_are_unavailable(double input)
    {
        Assert.Equal(PowerFormatter.Unavailable, PowerFormatter.FormatWatts(input));
    }
}

public class BytesPerSecondFormatterTests
{
    [Theory]
    [InlineData(0, "0.00 B/s")]
    [InlineData(500, "500 B/s")]
    [InlineData(999, "999 B/s")]
    [InlineData(1_000, "1.00 KB/s")]
    [InlineData(2_500, "2.50 KB/s")]
    [InlineData(1_500_000, "1.50 MB/s")]
    [InlineData(12_500_000, "12.5 MB/s")]
    [InlineData(2_500_000_000, "2.50 GB/s")]
    public void decimal_bytes_boundaries(double input, string expected)
    {
        Assert.Equal(expected, BytesPerSecondFormatter.Format(input, RateUnitSystem.DecimalBytes));
    }

    [Fact]
    public void decimal_bits_converts_bytes_to_bits()
    {
        // 1 MB/s = 8 Mbps
        Assert.Equal("8.00 Mbps", BytesPerSecondFormatter.Format(1_000_000, RateUnitSystem.DecimalBits));
    }

    [Fact]
    public void nan_is_warmup()
    {
        Assert.Equal(BytesPerSecondFormatter.WarmingUp, BytesPerSecondFormatter.Format(double.NaN));
    }

    [Fact]
    public void infinity_is_warmup()
    {
        Assert.Equal(BytesPerSecondFormatter.WarmingUp, BytesPerSecondFormatter.Format(double.PositiveInfinity));
    }

    [Theory]
    [InlineData(125, "1.00 Kbps")]
    [InlineData(1_250, "10.0 Kbps")]
    [InlineData(12_500, "100 Kbps")]
    [InlineData(125_000_000, "1.00 Gbps")]
    public void bits_boundaries(double bytesPerSecond, string expected)
    {
        Assert.Equal(expected, BytesPerSecondFormatter.Format(bytesPerSecond, RateUnitSystem.DecimalBits));
    }

    [Theory]
    [InlineData(0, "0.00 Kb/s")]
    [InlineData(125, "1.00 Kb/s")]
    [InlineData(1_250, "10.0 Kb/s")]
    [InlineData(12_500, "100 Kb/s")]
    [InlineData(125_000, "1000 Kb/s")]
    [InlineData(1_250_000_000, "10000000 Kb/s")]
    public void fixed_kilobits_never_changes_unit(double bytesPerSecond, string expected)
    {
        Assert.Equal(expected, BytesPerSecondFormatter.Format(bytesPerSecond, RateUnitSystem.FixedKilobitsPerSecond));
    }

    [Fact]
    public void negative_treated_as_zero()
    {
        Assert.Equal("0.00 B/s", BytesPerSecondFormatter.Format(-5));
    }
}

public class BytesFormatterTests
{
    [Fact]
    public void memory_pair_uses_binary_units()
    {
        // 512 MiB / 8 GiB
        long usage = 512L * 1024 * 1024;
        long limit = 8L * 1024 * 1024 * 1024;
        Assert.Equal("512 MiB / 8.00 GiB", BytesFormatter.FormatMemoryPair(usage, limit));
    }

    [Fact]
    public void negative_returns_na()
    {
        Assert.Equal(BytesFormatter.Unavailable, BytesFormatter.FormatMemoryPair(-1, 10));
        Assert.Equal(BytesFormatter.Unavailable, BytesFormatter.FormatBinaryMemory(-1));
    }

    [Theory]
    [InlineData(0, "0.00 B")]
    [InlineData(1024, "1.00 KiB")]
    [InlineData(10 * 1024, "10.0 KiB")]
    [InlineData(100 * 1024, "100 KiB")]
    [InlineData(1024 * 1024, "1.00 MiB")]
    public void binary_memory_boundaries(long bytes, string expected)
    {
        Assert.Equal(expected, BytesFormatter.FormatBinaryMemory(bytes));
    }

    [Fact]
    public void negative_limit_returns_na()
    {
        Assert.Equal(BytesFormatter.Unavailable, BytesFormatter.FormatMemoryPair(0, -1));
    }

    [Fact]
    public void zero_limit_returns_na()
    {
        Assert.Equal(BytesFormatter.Unavailable, BytesFormatter.FormatCompactMemoryPair(0, 0));
    }

    [Theory]
    [InlineData(500L * 1024 * 1024, 8L * 1024 * 1024 * 1024, "0.49/8.00 GiB")]
    [InlineData(12L * 1024 * 1024 * 1024, 16L * 1024 * 1024 * 1024, "12.0/16.0 GiB")]
    [InlineData(120L * 1024 * 1024 * 1024, 100L * 1024 * 1024 * 1024, "120/100 GiB")]
    [InlineData(512, 1024, "0.50/1.00 KiB")]
    public void memory_pair_uses_limit_unit_and_consistent_precision(long usage, long limit, string expected)
    {
        Assert.Equal(expected, BytesFormatter.FormatCompactMemoryPair(usage, limit));
    }
}
