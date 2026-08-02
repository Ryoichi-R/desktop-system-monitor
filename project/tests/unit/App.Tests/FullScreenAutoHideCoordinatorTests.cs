using DesktopSystemMonitor.App.Startup;
using DesktopSystemMonitor.Core.Settings;
using Xunit;

namespace DesktopSystemMonitor.App.Tests;

public sealed class FullScreenAutoHideCoordinatorTests
{
    [Fact]
    public void enter_and_exit_are_emitted_once()
    {
        var coordinator = new FullScreenAutoHideCoordinator();
        var settings = new AppSettings
        {
            LayerMode = WindowLayerMode.AlwaysOnTop,
            AutoHideOnFullScreen = true,
        };

        Assert.Equal(FullScreenVisibilityTransition.EnterHidden, coordinator.Evaluate(settings, true));
        Assert.Equal(FullScreenVisibilityTransition.None, coordinator.Evaluate(settings, true));
        Assert.True(coordinator.Hidden);
        Assert.Equal(FullScreenVisibilityTransition.ExitHidden, coordinator.Evaluate(settings, false));
        Assert.False(coordinator.Hidden);
    }

    [Theory]
    [InlineData(WindowLayerMode.OnDesktop, true)]
    [InlineData(WindowLayerMode.Normal, true)]
    [InlineData(WindowLayerMode.AlwaysOnTop, false)]
    public void disabled_policy_never_enters_hidden(WindowLayerMode layer, bool autoHide)
    {
        var coordinator = new FullScreenAutoHideCoordinator();
        var settings = new AppSettings { LayerMode = layer, AutoHideOnFullScreen = autoHide };

        Assert.Equal(FullScreenVisibilityTransition.None, coordinator.Evaluate(settings, true));
        Assert.False(coordinator.Hidden);
    }
}
