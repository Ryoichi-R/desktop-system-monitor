using DesktopSystemMonitor.App.Startup;
using Xunit;

namespace DesktopSystemMonitor.App.Tests;

public sealed class SamplingSuspensionCoordinatorTests
{
    [Fact]
    public void full_screen_pause_and_resume_are_emitted_once()
    {
        var coordinator = new SamplingSuspensionCoordinator();

        Assert.Equal(SamplingSuspensionTransition.Pause, coordinator.Evaluate(false, true));
        Assert.Equal(SamplingSuspensionTransition.None, coordinator.Evaluate(false, true));
        Assert.True(coordinator.Suspended);
        Assert.Equal(SamplingSuspensionTransition.Resume, coordinator.Evaluate(false, false));
        Assert.False(coordinator.Suspended);
    }

    [Fact]
    public void leaving_full_screen_does_not_resume_while_session_is_locked()
    {
        var coordinator = new SamplingSuspensionCoordinator();

        Assert.Equal(SamplingSuspensionTransition.Pause, coordinator.Evaluate(false, true));
        Assert.Equal(SamplingSuspensionTransition.None, coordinator.Evaluate(true, false));
        Assert.True(coordinator.Suspended);
        Assert.Equal(SamplingSuspensionTransition.Resume, coordinator.Evaluate(false, false));
    }

    [Fact]
    public void session_lock_pause_and_unlock_resume_are_emitted_once()
    {
        var coordinator = new SamplingSuspensionCoordinator();

        Assert.Equal(SamplingSuspensionTransition.Pause, coordinator.Evaluate(true, false));
        Assert.Equal(SamplingSuspensionTransition.None, coordinator.Evaluate(true, false));
        Assert.Equal(SamplingSuspensionTransition.Resume, coordinator.Evaluate(false, false));
    }
}
