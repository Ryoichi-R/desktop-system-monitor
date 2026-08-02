using DesktopSystemMonitor.Core.Layout;
using DesktopSystemMonitor.Core.Settings;
using Xunit;

namespace DesktopSystemMonitor.Core.Tests.Layout;

public sealed class WidgetWidthCalculatorTests
{
    [Fact]
    public void standard_width_preserves_the_existing_information_area()
    {
        Assert.Equal(280d, WidgetWidthCalculator.GetInformationWidth(WidgetDisplayMode.Standard));
        Assert.Equal(264d, WidgetWidthCalculator.GetContentWidth(WidgetDisplayMode.Standard));
    }

    [Fact]
    public void reduced_width_has_a_134_dip_content_area()
    {
        Assert.Equal(150d, WidgetWidthCalculator.GetInformationWidth(WidgetDisplayMode.Reduced));
        Assert.Equal(134d, WidgetWidthCalculator.GetContentWidth(WidgetDisplayMode.Reduced));
        Assert.Equal(8d, WidgetWidthCalculator.WidgetPaddingDip);
    }
}
