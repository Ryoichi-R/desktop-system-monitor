using DesktopSystemMonitor.App.Startup;
using DesktopSystemMonitor.Core.Settings;
using Xunit;

namespace DesktopSystemMonitor.App.Tests;

public sealed class BackgroundPresentationPolicyTests
{
    [Theory]
    [InlineData(WindowLayerMode.AlwaysOnTop, false, false, 0)]
    [InlineData(WindowLayerMode.Normal, false, true, 0)]
    [InlineData(WindowLayerMode.OnDesktop, false, true, 0)]
    [InlineData(WindowLayerMode.AlwaysOnTop, true, false, 1)]
    [InlineData(WindowLayerMode.Normal, true, false, 1)]
    [InlineData(WindowLayerMode.OnDesktop, true, false, 1)]
    [InlineData(WindowLayerMode.AlwaysOnTop, true, true, 2)]
    [InlineData(WindowLayerMode.Normal, true, true, 1)]
    [InlineData(WindowLayerMode.OnDesktop, true, true, 1)]
    public void mode_matrix_is_deterministic(
        WindowLayerMode layer,
        bool enabled,
        bool splitRequested,
        int expected)
    {
        var settings = new AppSettings
        {
            LayerMode = layer,
            BackgroundEnabled = enabled,
            BackgroundOpacity = 0.7,
            HideBackgroundBehindWindows = splitRequested,
        };

        Assert.Equal((BackgroundPresentationMode)expected, BackgroundPresentationPolicy.Evaluate(settings));
    }

    [Fact]
    public void zero_opacity_suppresses_both_background_presentations()
    {
        var settings = new AppSettings
        {
            BackgroundEnabled = true,
            BackgroundOpacity = 0,
            HideBackgroundBehindWindows = true,
        };

        Assert.Equal(BackgroundPresentationMode.None, BackgroundPresentationPolicy.Evaluate(settings));
    }
}
