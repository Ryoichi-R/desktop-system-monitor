using DesktopSystemMonitor.Core.Formatting;
using Xunit;

namespace DesktopSystemMonitor.Core.Tests.Formatting;

public sealed class OptionalTelemetryFormatterTests
{
    [Theory]
    [InlineData(99.5, "100°C")]
    [InlineData(-21, "N/A")]
    [InlineData(151, "N/A")]
    public void temperature_is_bounded(double value, string expected) =>
        Assert.Equal(expected, TemperatureFormatter.FormatCelsius(value));

    [Fact]
    public void duration_is_rounded_to_five_minutes() =>
        Assert.Equal("≈3h 40m", DurationFormatter.FormatApproximate(TimeSpan.FromMinutes(222)));
}
