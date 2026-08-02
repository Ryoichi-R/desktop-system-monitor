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
    public void settings_layout_change_reapplies_after_a_pending_reflow_completes()
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

        controller.ApplySettingsLayoutChange();

        Assert.False(controller.ReflowPending);
        Assert.False(reflowTimer.IsEnabled);
        Assert.InRange(surface.Left, 0, 1080 - surface.Width);
        Assert.InRange(surface.Top, 0, 1920 - surface.Height);
        Assert.Equal(1, settings.UpdateCount);
    }

    [Fact]
    public void settings_layout_change_does_not_take_position_during_user_move()
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
        double originalLeft = surface.Left;
        layout = SingleMonitor(1080, 1920);
        surface.RaiseDisplayConfigurationChanged();
        surface.RaiseUserMoveStarted();
        surface.Width = 150;

        controller.ApplySettingsLayoutChange();

        Assert.True(controller.ReflowPending);
        Assert.True(reflowTimer.IsEnabled);
        Assert.Equal(originalLeft, surface.Left);

        surface.Left = 300;
        surface.Top = 200;
        surface.RaiseUserMoveCompleted();
        controller.CompletePendingDisplayReflow();

        Assert.False(controller.ReflowPending);
        Assert.False(reflowTimer.IsEnabled);
        Assert.InRange(surface.Left, 0, 1080 - surface.Width);
        Assert.InRange(surface.Top, 0, 1920 - surface.Height);
        Assert.Equal(1, settings.UpdateCount);
    }

    [Theory]
    [InlineData(WindowPlacementAnchor.TopRight)]
    [InlineData(WindowPlacementAnchor.BottomRight)]
    [InlineData(WindowPlacementAnchor.TopLeft)]
    [InlineData(WindowPlacementAnchor.BottomLeft)]
    public void preset_anchor_is_recomputed_after_width_changes_from_280_to_150(
        WindowPlacementAnchor anchor)
    {
        var settings = new SettingsHolder
        {
            Value = new AppSettings
            {
                PlacementMode = WindowPlacementMode.Preset,
                PlacementAnchor = anchor,
                HorizontalMarginDip = 20,
                VerticalMarginDip = 30,
            }.Normalized(),
        };
        MonitorLayout layout = SingleMonitor(1920, 1080);
        var surface = new FakeSurface();
        using WindowPlacementController controller = CreateController(surface, () => layout, settings);
        controller.PositionWindow();

        surface.Width = 150;
        controller.ApplySettingsLayoutChange();

        Rect expected = WindowAnchor.Compute(
            surface.Width,
            surface.Height,
            layout.All,
            layout.Primary,
            settings.Value.SavedMonitorDeviceName,
            settings.Value.SavedRightEdgeDip,
            settings.Value.SavedTopEdgeDip,
            settings.Value.PlacementMode,
            settings.Value.PlacementAnchor,
            settings.Value.HorizontalMarginDip,
            settings.Value.VerticalMarginDip);
        Assert.Equal(expected.Left, surface.Left);
        Assert.Equal(expected.Top, surface.Top);
        Assert.Equal(WindowPlacementMode.Preset, settings.Value.PlacementMode);
        Assert.Equal(1, settings.UpdateCount);
    }

    [Fact]
    public void custom_right_and_top_edges_are_preserved_after_width_changes_from_280_to_150()
    {
        var settings = new SettingsHolder
        {
            Value = new AppSettings
            {
                SavedMonitorDeviceName = "DISPLAY1",
                SavedRightEdgeDip = 1800,
                SavedTopEdgeDip = 40,
                SavedMonitorDpi = 96,
                PlacementMode = WindowPlacementMode.Custom,
            }.Normalized(),
        };
        MonitorLayout layout = SingleMonitor(1920, 1080);
        var surface = new FakeSurface();
        using WindowPlacementController controller = CreateController(surface, () => layout, settings);
        controller.PositionWindow();

        surface.Width = 150;
        controller.ApplySettingsLayoutChange();

        Assert.Equal(1650, surface.Left);
        Assert.Equal(40, surface.Top);
        Assert.Equal(1800, surface.Left + surface.Width);
        Assert.Equal(WindowPlacementMode.Custom, settings.Value.PlacementMode);
        Assert.Equal(1, settings.UpdateCount);
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
            settings.Apply,
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
        public double Width { get; set; } = 280;
        public double Height { get; set; } = 200;
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
        public int UpdateCount { get; private set; }

        public void Apply(Func<AppSettings, AppSettings> update)
        {
            Value = update(Value);
            UpdateCount++;
        }
    }

    private sealed class FakeTimer : IPlacementTimer
    {
        public bool IsEnabled { get; private set; }
        public TimeSpan Interval { get; set; }
        public void Start() => IsEnabled = true;
        public void Stop() => IsEnabled = false;
    }
}
