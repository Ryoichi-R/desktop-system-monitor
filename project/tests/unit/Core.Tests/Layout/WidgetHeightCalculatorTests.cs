using DesktopSystemMonitor.Core.Layout;
using Xunit;

namespace DesktopSystemMonitor.Core.Tests.Layout;

public sealed class WidgetHeightCalculatorTests
{
    [Theory]
    [InlineData(2, 116d)]
    [InlineData(3, 168d)]
    [InlineData(4, 220d)]
    [InlineData(6, 324d)]
    public void calculates_logical_height(int rows, double expected) =>
        Assert.Equal(expected, WidgetHeightCalculator.Calculate(rows));

    [Fact]
    public void rejects_fewer_than_two_rows() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => WidgetHeightCalculator.Calculate(1));

    [Fact]
    public void legacy_overload_remains_obsolete_and_compatible()
    {
#pragma warning disable CS0618
        Assert.Equal(324d, WidgetHeightCalculator.Calculate(true, true, true));
#pragma warning restore CS0618
        Assert.NotNull(typeof(WidgetHeightCalculator).GetMethod(nameof(WidgetHeightCalculator.Calculate), [typeof(bool), typeof(bool), typeof(bool)])!
            .GetCustomAttributes(typeof(ObsoleteAttribute), false).SingleOrDefault());
    }
}
