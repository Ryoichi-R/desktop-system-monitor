using System;
using DesktopSystemMonitor.App.Startup;
using DesktopSystemMonitor.Core.Layout;
using Xunit;

namespace DesktopSystemMonitor.App.Tests;

public sealed class DisplayReflowPolicyTests
{
    [Theory]
    [InlineData(0x007E, 0, true)]
    [InlineData(0x001A, 0x002F, true)]
    [InlineData(0x001A, 0, false)]
    [InlineData(0x0006, 0, false)]
    public void only_display_and_work_area_messages_request_reflow(
        int message,
        long wParam,
        bool expected)
    {
        Assert.Equal(
            expected,
            MainWindow.IsDisplayConfigurationChangeMessage(message, new IntPtr(wParam)));
    }

    [Fact]
    public void debounce_uses_trailing_delay_at_start_of_wave()
    {
        DateTimeOffset started = DateTimeOffset.UtcNow;

        TimeSpan delay = DisplayReflowPolicy.ComputeDelay(
            started,
            started,
            TimeSpan.FromMilliseconds(750),
            TimeSpan.FromSeconds(2));

        Assert.Equal(TimeSpan.FromMilliseconds(750), delay);
    }

    [Fact]
    public void debounce_is_shortened_by_maximum_wave_deadline()
    {
        DateTimeOffset started = DateTimeOffset.UtcNow;

        TimeSpan delay = DisplayReflowPolicy.ComputeDelay(
            started,
            started.AddMilliseconds(1_800),
            TimeSpan.FromMilliseconds(750),
            TimeSpan.FromSeconds(2));

        Assert.Equal(TimeSpan.FromMilliseconds(200), delay);
    }

    [Fact]
    public void elapsed_wave_uses_minimum_positive_dispatcher_delay()
    {
        DateTimeOffset started = DateTimeOffset.UtcNow;

        TimeSpan delay = DisplayReflowPolicy.ComputeDelay(
            started,
            started.AddSeconds(3),
            TimeSpan.FromMilliseconds(750),
            TimeSpan.FromSeconds(2));

        Assert.Equal(TimeSpan.FromMilliseconds(1), delay);
    }

    [Theory]
    [InlineData(false, false, false, true)]
    [InlineData(true, false, false, false)]
    [InlineData(false, true, false, false)]
    [InlineData(false, false, true, false)]
    public void persistence_requires_stable_layout_and_no_active_move(
        bool reflowPending,
        bool userMoveInProgress,
        bool layoutChanged,
        bool expected)
    {
        Assert.Equal(
            expected,
            DisplayReflowPolicy.CanPersistPosition(
                reflowPending,
                userMoveInProgress,
                layoutChanged));
    }

    [Fact]
    public void layout_fingerprint_ignores_order_and_device_name_case()
    {
        MonitorInfo[] cached =
        [
            new("DISPLAY1", new Rect(0, 0, 900, 1400), 192),
            new("DISPLAY2", new Rect(-800, 0, 800, 600), 96),
        ];
        MonitorInfo[] current =
        [
            new("display2", new Rect(-800, 0, 800, 600), 96),
            new("display1", new Rect(0, 0, 900, 1400), 192),
        ];

        Assert.True(WindowPlacementController.LayoutsEquivalent(cached, current));
    }

    [Fact]
    public void layout_fingerprint_detects_work_area_change()
    {
        MonitorInfo[] cached = [new("DISPLAY1", new Rect(0, 0, 1400, 900), 192)];
        MonitorInfo[] current = [new("DISPLAY1", new Rect(0, 0, 900, 1400), 192)];

        Assert.False(WindowPlacementController.LayoutsEquivalent(cached, current));
    }

    [Fact]
    public void duplicate_notifications_share_one_wave_and_respect_maximum_delay()
    {
        DateTimeOffset started = new(2026, 7, 12, 0, 0, 0, TimeSpan.Zero);
        var coordinator = CreateCoordinator();

        Assert.Equal(TimeSpan.FromMilliseconds(750), coordinator.BeginWave(started));
        Assert.Equal(
            TimeSpan.FromMilliseconds(750),
            coordinator.BeginWave(started.AddMilliseconds(500)));
        Assert.Equal(
            TimeSpan.FromMilliseconds(200),
            coordinator.BeginWave(started.AddMilliseconds(1_800)));

        Assert.Equal(DisplayReflowCompletionDecision.Reflow, coordinator.RequestCompletion());
        coordinator.FinishCompletion();
        Assert.Equal(DisplayReflowCompletionDecision.Ignore, coordinator.RequestCompletion());
        Assert.True(coordinator.CanPersist(layoutChanged: false));
    }

    [Fact]
    public void location_change_before_display_message_cannot_persist_changed_layout()
    {
        var coordinator = CreateCoordinator();

        Assert.False(coordinator.CanPersist(layoutChanged: true));
        coordinator.BeginWave(DateTimeOffset.UtcNow);
        Assert.False(coordinator.CanPersist(layoutChanged: false));
    }

    [Fact]
    public void user_drag_defers_reflow_without_losing_pending_wave()
    {
        var coordinator = CreateCoordinator();
        coordinator.BeginWave(DateTimeOffset.UtcNow);
        coordinator.BeginUserMove();

        Assert.Equal(
            DisplayReflowCompletionDecision.RetryAfterUserMove,
            coordinator.RequestCompletion());
        Assert.True(coordinator.Pending);
        Assert.False(coordinator.CanPersist(layoutChanged: false));

        coordinator.EndUserMove();
        Assert.Equal(DisplayReflowCompletionDecision.Reflow, coordinator.RequestCompletion());
        coordinator.FinishCompletion();
        Assert.True(coordinator.CanPersist(layoutChanged: false));
    }

    [Fact]
    public void shutdown_suppresses_pending_completion_and_position_flush()
    {
        var coordinator = CreateCoordinator();
        coordinator.BeginWave(DateTimeOffset.UtcNow);

        coordinator.BeginShutdown();

        Assert.Equal(DisplayReflowCompletionDecision.Ignore, coordinator.RequestCompletion());
        Assert.False(coordinator.CanPersist(layoutChanged: false));
        Assert.Null(coordinator.BeginWave(DateTimeOffset.UtcNow.AddSeconds(1)));
    }

    [Fact]
    public void stable_position_is_flushable_immediately_before_shutdown()
    {
        var coordinator = CreateCoordinator();
        int saves = 0;

        if (coordinator.CanPersist(layoutChanged: false))
        {
            saves++;
        }
        coordinator.BeginShutdown();

        Assert.Equal(1, saves);
        Assert.False(coordinator.CanPersist(layoutChanged: false));
    }

    [Fact]
    public void pending_reflow_is_not_flushable_before_shutdown()
    {
        var coordinator = CreateCoordinator();
        coordinator.BeginWave(DateTimeOffset.UtcNow);
        int saves = 0;

        if (coordinator.CanPersist(layoutChanged: false))
        {
            saves++;
        }
        coordinator.BeginShutdown();

        Assert.Equal(0, saves);
        Assert.Equal(DisplayReflowCompletionDecision.Ignore, coordinator.RequestCompletion());
    }

    [Fact]
    public void immediate_settings_apply_can_complete_pending_wave_only_once()
    {
        var coordinator = CreateCoordinator();
        coordinator.BeginWave(DateTimeOffset.UtcNow);

        Assert.Equal(DisplayReflowCompletionDecision.Reflow, coordinator.RequestCompletion());
        coordinator.FinishCompletion();

        Assert.Equal(DisplayReflowCompletionDecision.Ignore, coordinator.RequestCompletion());
    }

    private static DisplayReflowCoordinator CreateCoordinator() => new(
        TimeSpan.FromMilliseconds(750),
        TimeSpan.FromSeconds(2));
}
