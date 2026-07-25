using System;
using System.Collections.Generic;
using DesktopSystemMonitor.App.Startup;
using DesktopSystemMonitor.Core.Layout;
using DesktopSystemMonitor.Core.Settings;
using Xunit;

namespace DesktopSystemMonitor.App.Tests;

public sealed class WindowPlacementControllerTests
{
    [Fact]
    public void initial_position_uses_window_anchor_without_persisting()
    {
        var settings = new SettingsHolder();
        MonitorLayout layout = SingleMonitor(1920, 1080);
        var surface = new FakeSurface();
        using WindowPlacementController controller = CreateController(surface, () => layout, settings);

        controller.PositionWindow();

        Rect expected = WindowAnchor.Compute(
            surface.Width,
            surface.Height,
            layout.All,
            layout.Primary,
            null,
            null,
            null);
        Assert.Equal(expected.Left, surface.Left);
        Assert.Equal(expected.Top, surface.Top);
        Assert.Null(settings.Value.SavedMonitorDeviceName);
    }

    [Fact]
    public void debounced_location_change_persists_the_stable_position()
    {
        var settings = new SettingsHolder();
        MonitorLayout layout = SingleMonitor(1920, 1080);
        var surface = new FakeSurface();
        var persistTimer = new FakeTimer();
        using WindowPlacementController controller = CreateController(
            surface,
            () => layout,
            settings,
            persistTimer);
        controller.PositionWindow();
        surface.Left = 400;
        surface.Top = 250;

        surface.RaiseLocationChanged();
        controller.FlushPendingWindowPosition();

        Assert.False(persistTimer.IsEnabled);
        Assert.Equal("DISPLAY1", settings.Value.SavedMonitorDeviceName);
        Assert.Equal(WindowPlacementMode.Custom, settings.Value.PlacementMode);
        Assert.Equal(680, settings.Value.SavedRightEdgeDip);
        Assert.Equal(250, settings.Value.SavedTopEdgeDip);
    }

    [Fact]
    public void display_change_reflows_and_persists_once()
    {
        var settings = new SettingsHolder();
        MonitorLayout layout = SingleMonitor(1920, 1080);
        var surface = new FakeSurface();
        var reflowTimer = new FakeTimer();
        using WindowPlacementController controller = CreateController(
            surface,
            () => layout,
            settings,
            displayReflowTimer: reflowTimer);
        controller.PositionWindow();
        layout = SingleMonitor(1080, 1920);

        surface.RaiseDisplayConfigurationChanged();
        Assert.True(reflowTimer.IsEnabled);
        controller.CompletePendingDisplayReflow();

        Assert.False(reflowTimer.IsEnabled);
        Assert.InRange(surface.Left, 0, 1080 - surface.Width);
        Assert.InRange(surface.Top, 0, 1920 - surface.Height);
        Assert.Null(settings.Value.SavedMonitorDeviceName);
        Assert.Equal(WindowPlacementMode.Preset, settings.Value.PlacementMode);
    }

    [Fact]
    public void shutdown_flushes_a_pending_stable_move_and_stops_timers()
    {
        var settings = new SettingsHolder();
        MonitorLayout layout = SingleMonitor(1920, 1080);
        var surface = new FakeSurface();
        var persistTimer = new FakeTimer();
        var reflowTimer = new FakeTimer();
        using WindowPlacementController controller = CreateController(
            surface,
            () => layout,
            settings,
            persistTimer,
            reflowTimer);
        controller.PositionWindow();
        surface.Left = 100;
        surface.RaiseLocationChanged();

        controller.FlushBeforeShutdown();

        Assert.False(persistTimer.IsEnabled);
        Assert.False(reflowTimer.IsEnabled);
        Assert.Equal(380, settings.Value.SavedRightEdgeDip);
    }

    private static WindowPlacementController CreateController(
        FakeSurface surface,
        Func<MonitorLayout> monitors,
        SettingsHolder settings,
        FakeTimer persistTimer = null,
        FakeTimer displayReflowTimer = null)
    {
        persistTimer ??= new FakeTimer();
        displayReflowTimer ??= new FakeTimer();
        return new WindowPlacementController(
            surface,
            () => settings.Value,
            update => settings.Value = update(settings.Value),
            monitors,
            () => new DateTimeOffset(2026, 7, 17, 0, 0, 0, TimeSpan.Zero),
            (_, _) => { },
            persistTimer,
            displayReflowTimer);
    }

    private static MonitorLayout SingleMonitor(double width, double height)
    {
        var monitor = new MonitorInfo("DISPLAY1", new Rect(0, 0, width, height), 96);
        return new MonitorLayout([monitor], monitor);
    }

    private sealed class FakeSurface : IWindowPlacementSurface
    {
        public double Left { get; set; }
        public double Top { get; set; }
        public double Width { get; } = 280;
        public double Height { get; } = 200;
        public bool IsLoaded { get; } = true;
        public event Action LocationChanged;
        public event Action Closing;
        public event Action DisplayConfigurationChanged;
        public event Action UserMoveStarted;
        public event Action UserMoveCompleted;
        public void RaiseLocationChanged() => LocationChanged?.Invoke();
        public void RaiseClosing() => Closing?.Invoke();
        public void RaiseDisplayConfigurationChanged() => DisplayConfigurationChanged?.Invoke();
        public void RaiseUserMoveStarted() => UserMoveStarted?.Invoke();
        public void RaiseUserMoveCompleted() => UserMoveCompleted?.Invoke();
    }

    private sealed class SettingsHolder
    {
        public AppSettings Value { get; set; } = new();
    }

    private sealed class FakeTimer : IPlacementTimer
    {
        public bool IsEnabled { get; private set; }
        public TimeSpan Interval { get; set; }
        public void Start() => IsEnabled = true;
        public void Stop() => IsEnabled = false;
    }
}
