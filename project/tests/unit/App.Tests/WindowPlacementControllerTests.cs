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
        Assert.NotNull(settings.Value.PlacementXRatio);
        Assert.NotNull(settings.Value.PlacementYRatio);
        Assert.Equal((400 - WindowAnchor.Margin) / (1920 - 280 - (2 * WindowAnchor.Margin)), settings.Value.PlacementXRatio!.Value, 6);
        Assert.Equal((250 - WindowAnchor.Margin) / (1080 - 200 - (2 * WindowAnchor.Margin)), settings.Value.PlacementYRatio!.Value, 6);
        Assert.Equal(1920, settings.Value.SavedWorkAreaWidthDip);
        Assert.Equal(1080, settings.Value.SavedWorkAreaHeightDip);
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
    public void migrated_custom_ratio_commits_on_drag_not_startup()
    {
        var settings = new SettingsHolder
        {
            Value = new AppSettings
            {
                SchemaVersion = 4,
                SavedMonitorDeviceName = "DISPLAY1",
                SavedRightEdgeDip = 1392,
                SavedTopEdgeDip = 8,
                PlacementMode = WindowPlacementMode.Custom,
            },
        };
        MonitorLayout landscape = SingleMonitor(1400, 900);
        var surface = new FakeSurface();
        using WindowPlacementController controller = CreateController(surface, () => landscape, settings);

        controller.PositionWindow();

        Assert.Equal(0, settings.UpdateCount);
        Assert.Null(settings.Value.PlacementXRatio);
        Assert.Null(settings.Value.SavedWorkAreaWidthDip);

        surface.RaiseUserMoveStarted();
        surface.Left = landscape.Primary.WorkArea.Right - surface.Width - WindowAnchor.Margin;
        surface.Top = WindowAnchor.Margin;
        surface.RaiseUserMoveCompleted();
        controller.FlushPendingWindowPosition();

        Assert.Equal(WindowPlacementMode.Custom, settings.Value.PlacementMode);
        Assert.Equal(1, settings.Value.PlacementXRatio);
        Assert.Equal(0, settings.Value.PlacementYRatio);
        Assert.Equal(1400, settings.Value.SavedWorkAreaWidthDip);
        Assert.Equal(900, settings.Value.SavedWorkAreaHeightDip);

        MonitorLayout portrait = SingleMonitor(900, 1400);
        var restartedSurface = new FakeSurface();
        using WindowPlacementController restartedController = CreateController(
            restartedSurface,
            () => portrait,
            settings);

        restartedController.PositionWindow();

        Assert.Equal(900 - restartedSurface.Width - WindowAnchor.Margin, restartedSurface.Left);
        Assert.Equal(WindowAnchor.Margin, restartedSurface.Top);
    }

    [Fact]
    public void reflow_persist_keeps_ratio_and_updates_absolute_edges()
    {
        var settings = new SettingsHolder
        {
            Value = new AppSettings
            {
                SavedMonitorDeviceName = "DISPLAY1",
                SavedRightEdgeDip = 1392,
                SavedTopEdgeDip = 8,
                PlacementMode = WindowPlacementMode.Custom,
                PlacementXRatio = 1,
                PlacementYRatio = 0,
                SavedWorkAreaWidthDip = 1400,
                SavedWorkAreaHeightDip = 900,
            }.Normalized(),
        };
        MonitorLayout layout = SingleMonitor(1400, 900);
        var surface = new FakeSurface();
        var reflowTimer = new FakeTimer();
        using WindowPlacementController controller = CreateController(
            surface,
            () => layout,
            settings,
            displayReflowTimer: reflowTimer);
        controller.PositionWindow();
        layout = SingleMonitor(900, 1400);

        surface.RaiseDisplayConfigurationChanged();
        controller.CompletePendingDisplayReflow();

        Assert.Equal(1, settings.Value.PlacementXRatio);
        Assert.Equal(0, settings.Value.PlacementYRatio);
        Assert.Equal(900, settings.Value.SavedWorkAreaWidthDip);
        Assert.Equal(1400, settings.Value.SavedWorkAreaHeightDip);
        Assert.Equal(900 - surface.Width - WindowAnchor.Margin, surface.Left);
        Assert.Equal(WindowAnchor.Margin, surface.Top);
        Assert.Equal(surface.Left + surface.Width, settings.Value.SavedRightEdgeDip);
    }

    [Fact]
    public void preset_mode_reflow_uses_anchor_not_intent()
    {
        var settings = new SettingsHolder
        {
            Value = new AppSettings
            {
                PlacementMode = WindowPlacementMode.Preset,
                PlacementAnchor = WindowPlacementAnchor.TopRight,
                HorizontalMarginDip = 24,
                VerticalMarginDip = 24,
            }.Normalized(),
        };
        MonitorLayout layout = SingleMonitor(1400, 900);
        var surface = new FakeSurface();
        var reflowTimer = new FakeTimer();
        using WindowPlacementController controller = CreateController(
            surface,
            () => layout,
            settings,
            displayReflowTimer: reflowTimer);
        controller.PositionWindow();
        layout = SingleMonitor(900, 1400);

        surface.RaiseDisplayConfigurationChanged();
        controller.CompletePendingDisplayReflow();

        Assert.Equal(900 - surface.Width - 24, surface.Left);
        Assert.Equal(24, surface.Top);
        Assert.Equal(1, settings.UpdateCount);
        Assert.Null(settings.Value.PlacementXRatio);
        Assert.Null(settings.Value.SavedWorkAreaWidthDip);
    }

    [Fact]
    public void capture_failure_clears_ratio_fields()
    {
        var tiny = new MonitorInfo("DISPLAY1", new Rect(0, 0, 100, 100), 96);
        var settings = new SettingsHolder
        {
            Value = new AppSettings
            {
                PlacementMode = WindowPlacementMode.Custom,
                PlacementXRatio = 0.5,
                PlacementYRatio = 0.5,
                SavedWorkAreaWidthDip = 1000,
                SavedWorkAreaHeightDip = 1000,
            }.Normalized(),
        };
        MonitorLayout layout = new([tiny], tiny);
        var surface = new FakeSurface();
        using WindowPlacementController controller = CreateController(surface, () => layout, settings);

        controller.PersistWindowPosition();

        Assert.Null(settings.Value.PlacementXRatio);
        Assert.Null(settings.Value.PlacementYRatio);
        Assert.Null(settings.Value.SavedWorkAreaWidthDip);
        Assert.Null(settings.Value.SavedWorkAreaHeightDip);
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

    [Theory]
    [InlineData(1.0, 0.0)]
    [InlineData(0.35, 0.6)]
    public void migrated_json_drag_reflow_and_restart_preserve_placement(double xRatio, double yRatio)
    {
        string directory = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "dsm-placement-" + Guid.NewGuid().ToString("N"));
        System.IO.Directory.CreateDirectory(directory);
        string path = System.IO.Path.Combine(directory, "settings.json");
        try
        {
            const string legacy = """{"SchemaVersion":4,"PlacementMode":"Custom","SavedMonitorDeviceName":"DISPLAY1","SavedRightEdgeDip":711.43,"SavedTopEdgeDip":8,"FuturePlacementNote":"keep"}""";
            System.IO.File.WriteAllText(path, legacy);
            var store = new FileSystemSettingsStore(path);
            var settings = new SettingsHolder { Value = store.Load() };
            Assert.Equal(5, settings.Value.SchemaVersion);
            MonitorLayout layout = SingleMonitor(1400, 900);
            var surface = new FakeSurface();
            using (WindowPlacementController controller = CreateController(surface, () => layout, settings))
            {
                controller.PositionWindow();
                Assert.Equal(711.43 - surface.Width, surface.Left, 6);
                Assert.Equal(0, settings.UpdateCount);
                Assert.Null(settings.Value.PlacementXRatio);
                Assert.Null(settings.Value.PlacementYRatio);
                Assert.Null(settings.Value.SavedWorkAreaWidthDip);
                Assert.Null(settings.Value.SavedWorkAreaHeightDip);
                Assert.Equal(legacy, System.IO.File.ReadAllText(path));

                surface.RaiseUserMoveStarted();
                surface.Left = 8 + (1400 - surface.Width - 16) * xRatio;
                surface.Top = 8 + (900 - surface.Height - 16) * yRatio;
                surface.RaiseUserMoveCompleted();
                controller.FlushPendingWindowPosition();
                store.Save(settings.Value);
                settings.Value = new FileSystemSettingsStore(path).Load();
                Assert.Equal(WindowPlacementMode.Custom, settings.Value.PlacementMode);
                Assert.Equal(xRatio, settings.Value.PlacementXRatio!.Value, 6);
                Assert.Equal(yRatio, settings.Value.PlacementYRatio!.Value, 6);
                Assert.Equal(1400, settings.Value.SavedWorkAreaWidthDip);
                Assert.Equal(900, settings.Value.SavedWorkAreaHeightDip);

                layout = SingleMonitor(900, 1400);
                surface.RaiseDisplayConfigurationChanged();
                controller.CompletePendingDisplayReflow();
                Assert.Equal(8 + (900 - surface.Width - 16) * xRatio, surface.Left, 6);
                Assert.Equal(8 + (1400 - surface.Height - 16) * yRatio, surface.Top, 6);
                store.Save(settings.Value);
            }
            var restarted = new SettingsHolder { Value = new FileSystemSettingsStore(path).Load() };
            Assert.Equal(900, restarted.Value.SavedWorkAreaWidthDip);
            Assert.Equal(1400, restarted.Value.SavedWorkAreaHeightDip);
            Assert.Equal(xRatio, restarted.Value.PlacementXRatio!.Value, 6);
            Assert.Equal(yRatio, restarted.Value.PlacementYRatio!.Value, 6);
            layout = SingleMonitor(1400, 900);
            var restartedSurface = new FakeSurface();
            using WindowPlacementController restartedController = CreateController(restartedSurface, () => layout, restarted);
            restartedController.PositionWindow();
            Assert.Equal(8 + (1400 - restartedSurface.Width - 16) * xRatio, restartedSurface.Left, 6);
            Assert.Equal(8 + (900 - restartedSurface.Height - 16) * yRatio, restartedSurface.Top, 6);
            Assert.Equal(0, restarted.UpdateCount);
            using var document = System.Text.Json.JsonDocument.Parse(System.IO.File.ReadAllText(path));
            Assert.Equal("keep", document.RootElement.GetProperty("FuturePlacementNote").GetString());
        }
        finally
        {
            System.IO.Directory.Delete(directory, recursive: true);
        }
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
