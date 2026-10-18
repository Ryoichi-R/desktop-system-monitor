using DesktopSystemMonitor.Core.Layout;
using DesktopSystemMonitor.Core.Settings;
using Xunit;

namespace DesktopSystemMonitor.Core.Tests.Layout;

public class WindowAnchorTests
{
    [Theory]
    [InlineData(WindowPlacementAnchor.TopRight, 1620, 20)]
    [InlineData(WindowPlacementAnchor.BottomRight, 1620, 870)]
    [InlineData(WindowPlacementAnchor.TopLeft, 20, 20)]
    [InlineData(WindowPlacementAnchor.BottomLeft, 20, 870)]
    public void preset_position_uses_selected_corner_and_margins(
        WindowPlacementAnchor anchor,
        double expectedLeft,
        double expectedTop)
    {
        MonitorInfo primary = Primary();
        Rect rect = WindowAnchor.Compute(
            280,
            150,
            [primary],
            primary,
            null,
            null,
            null,
            WindowPlacementMode.Preset,
            anchor,
            20,
            20);

        Assert.Equal(expectedLeft, rect.Left);
        Assert.Equal(expectedTop, rect.Top);
    }

    private static MonitorInfo Primary() => new(
        DeviceName: @"\\.\DISPLAY1",
        WorkArea: new Rect(0, 0, 1920, 1040),
        Dpi: 96);

    private static MonitorInfo Secondary() => new(
        DeviceName: @"\\.\DISPLAY2",
        WorkArea: new Rect(1920, 0, 2560, 1400),
        Dpi: 96);

    [Fact]
    public void no_saved_position_anchors_to_primary_top_right_with_margin()
    {
        var rect = WindowAnchor.Compute(280, 156, new[] { Primary() }, Primary(), null, null, null);
        Assert.Equal(1920 - 280 - WindowAnchor.Margin, rect.Left);
        Assert.Equal(WindowAnchor.Margin, rect.Top);
    }

    [Fact]
    public void saved_position_is_honored_when_monitor_still_present()
    {
        var rect = WindowAnchor.Compute(280, 156,
            monitors: new[] { Primary() },
            primary: Primary(),
            savedDeviceName: @"\\.\DISPLAY1",
            savedRightEdge: 1000,
            savedTopEdge: 100);
        Assert.Equal(720, rect.Left);
        Assert.Equal(100, rect.Top);
    }

    [Fact]
    public void custom_position_reflows_by_ratio_when_work_area_size_changed()
    {
        var portrait = new MonitorInfo("DISPLAY1", new Rect(0, 0, 900, 1400), 192);

        Rect rect = WindowAnchor.Compute(
            widgetWidth: 280,
            widgetHeight: 220,
            monitors: [portrait],
            primary: portrait,
            savedDeviceName: "DISPLAY1",
            savedRightEdge: 1392,
            savedTopEdge: 8,
            placementMode: WindowPlacementMode.Custom,
            placementAnchor: WindowPlacementAnchor.TopRight,
            horizontalMargin: 8,
            verticalMargin: 8,
            savedWorkAreaWidth: 1400,
            savedWorkAreaHeight: 900,
            savedXRatio: 1,
            savedYRatio: 0);

        Assert.Equal(900 - 280 - WindowAnchor.Margin, rect.Left);
        Assert.Equal(WindowAnchor.Margin, rect.Top);
    }

    [Fact]
    public void custom_position_keeps_absolute_edges_when_work_area_unchanged()
    {
        MonitorInfo monitor = new("DISPLAY1", new Rect(0, 0, 1400, 900), 192);

        Rect rect = WindowAnchor.Compute(
            280,
            220,
            [monitor],
            monitor,
            "DISPLAY1",
            1234,
            44,
            WindowPlacementMode.Custom,
            WindowPlacementAnchor.TopRight,
            WindowAnchor.Margin,
            WindowAnchor.Margin,
            1400,
            900,
            0.95,
            0.1);

        Assert.Equal(954, rect.Left);
        Assert.Equal(44, rect.Top);
    }

    [Fact]
    public void landscape_portrait_landscape_round_trip_through_settings()
    {
        MonitorInfo landscape = new("DISPLAY1", new Rect(0, 0, 1400, 900), 192);
        MonitorInfo portrait = new("DISPLAY1", new Rect(0, 0, 900, 1400), 192);
        AppSettings persisted = new AppSettings
        {
            SavedMonitorDeviceName = landscape.DeviceName,
            SavedRightEdgeDip = landscape.WorkArea.Right - WindowAnchor.Margin,
            SavedTopEdgeDip = WindowAnchor.Margin,
            PlacementMode = WindowPlacementMode.Custom,
            PlacementXRatio = 1,
            PlacementYRatio = 0,
            SavedWorkAreaWidthDip = landscape.WorkArea.Width,
            SavedWorkAreaHeightDip = landscape.WorkArea.Height,
        }.Normalized();

        Rect rotated = ComputeWithSettings(280, 220, [portrait], portrait, persisted);
        AppSettings persistedAfterRotation = (persisted with
        {
            SavedRightEdgeDip = rotated.Right,
            SavedTopEdgeDip = rotated.Top,
            SavedWorkAreaWidthDip = portrait.WorkArea.Width,
            SavedWorkAreaHeightDip = portrait.WorkArea.Height,
        }).Normalized();
        Rect restored = ComputeWithSettings(280, 220, [landscape], landscape, persistedAfterRotation);

        Assert.Equal(portrait.WorkArea.Right - 280 - WindowAnchor.Margin, rotated.Left);
        Assert.Equal(landscape.WorkArea.Right - 280 - WindowAnchor.Margin, restored.Left);
        Assert.Equal(WindowAnchor.Margin, restored.Top);
    }

    [Theory]
    [InlineData(double.NaN, 0.5)]
    [InlineData(-0.1, 0.5)]
    [InlineData(0.5, 1.1)]
    [InlineData(null, 0.5)]
    public void invalid_saved_ratio_falls_back_to_absolute_edges(double? xRatio, double? yRatio)
    {
        MonitorInfo monitor = new("DISPLAY1", new Rect(0, 0, 1400, 900), 192);

        Rect rect = WindowAnchor.Compute(
            280,
            220,
            [monitor],
            monitor,
            "DISPLAY1",
            1000,
            100,
            WindowPlacementMode.Custom,
            WindowPlacementAnchor.TopRight,
            WindowAnchor.Margin,
            WindowAnchor.Margin,
            1200,
            800,
            xRatio,
            yRatio);

        Assert.Equal(720, rect.Left);
        Assert.Equal(100, rect.Top);
    }

    [Fact]
    public void saved_position_off_screen_is_clamped_back_into_working_area()
    {
        // Saved position would place the widget entirely to the right of a
        // now-smaller monitor.
        var smallMonitor = new MonitorInfo(@"\\.\DISPLAY1", new Rect(0, 0, 800, 600), 96);
        var rect = WindowAnchor.Compute(280, 156,
            monitors: new[] { smallMonitor },
            primary: smallMonitor,
            savedDeviceName: @"\\.\DISPLAY1",
            savedRightEdge: 5000,
            savedTopEdge: 3000);
        Assert.True(rect.Right <= 800 - WindowAnchor.Margin + 0.001);
        Assert.True(rect.Bottom <= 600 - WindowAnchor.Margin + 0.001);
        Assert.True(rect.Left >= WindowAnchor.Margin);
    }

    [Fact]
    public void missing_saved_monitor_falls_back_to_primary()
    {
        var rect = WindowAnchor.Compute(280, 156,
            monitors: new[] { Primary() },
            primary: Primary(),
            savedDeviceName: @"\\.\DISPLAY-GONE",
            savedRightEdge: null,
            savedTopEdge: null);
        Assert.Equal(1920 - 280 - WindowAnchor.Margin, rect.Left);
    }

    [Fact]
    public void multi_monitor_uses_saved_device()
    {
        var rect = WindowAnchor.Compute(280, 156,
            monitors: new[] { Primary(), Secondary() },
            primary: Primary(),
            savedDeviceName: @"\\.\DISPLAY2",
            savedRightEdge: null,
            savedTopEdge: null);
        Assert.True(rect.Left >= 1920);
    }

    [Fact]
    public void rejects_non_positive_widget_dimensions()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => WindowAnchor.Compute(0, 100, [Primary()], Primary(), null, null, null));
        Assert.Throws<ArgumentOutOfRangeException>(() => WindowAnchor.Compute(100, -1, [Primary()], Primary(), null, null, null));
    }

    [Fact]
    public void widget_larger_than_work_area_stays_anchored_at_margin()
    {
        var tiny = new MonitorInfo("tiny", new Rect(0, 0, 100, 100), 96);
        Rect rect = WindowAnchor.Compute(280, 156, [tiny], tiny, null, null, null);
        Assert.Equal(WindowAnchor.Margin, rect.Left);
        Assert.Equal(WindowAnchor.Margin, rect.Top);
    }

    [Fact]
    public void right_top_intent_survives_landscape_portrait_round_trip()
    {
        var landscape = new MonitorInfo("DISPLAY1", new Rect(0, 0, 1400, 900), 192);
        var portrait = new MonitorInfo("DISPLAY1", new Rect(0, 0, 900, 1400), 192);
        var original = new Rect(
            landscape.WorkArea.Right - 280 - WindowAnchor.Margin,
            landscape.WorkArea.Top + WindowAnchor.Margin,
            280,
            220);

        Assert.True(WindowAnchor.TryCaptureIntent(original, landscape, out WindowPlacementIntent intent));
        Assert.True(WindowAnchor.TryComputeFromIntent(280, 220, [portrait], portrait, intent, out Rect rotated));
        Assert.True(WindowAnchor.TryComputeFromIntent(280, 220, [landscape], landscape, intent, out Rect restored));

        Assert.Equal(portrait.WorkArea.Right - 280 - WindowAnchor.Margin, rotated.Left, 6);
        Assert.Equal(WindowAnchor.Margin, rotated.Top, 6);
        Assert.Equal(original, restored);
    }

    [Fact]
    public void automatic_clamp_does_not_need_to_replace_the_original_intent()
    {
        var wide = new MonitorInfo("DISPLAY1", new Rect(0, 0, 1600, 900), 96);
        var narrow = new MonitorInfo("DISPLAY1", new Rect(0, 0, 500, 900), 96);
        var original = new Rect(1200, 250, 280, 220);

        Assert.True(WindowAnchor.TryCaptureIntent(original, wide, out WindowPlacementIntent intent));
        Assert.True(WindowAnchor.TryComputeFromIntent(280, 220, [narrow], narrow, intent, out Rect narrowRect));
        Assert.True(WindowAnchor.TryComputeFromIntent(280, 220, [wide], wide, intent, out Rect restored));

        Assert.True(narrowRect.Right <= narrow.WorkArea.Right - WindowAnchor.Margin + 0.001);
        Assert.Equal(original.Left, restored.Left, 6);
        Assert.Equal(original.Top, restored.Top, 6);
    }

    [Fact]
    public void intent_preserves_non_corner_relative_position()
    {
        var oldMonitor = new MonitorInfo("DISPLAY1", new Rect(0, 0, 1200, 800), 96);
        var newMonitor = new MonitorInfo("DISPLAY1", new Rect(0, 0, 800, 1200), 144);
        double oldMinLeft = WindowAnchor.Margin;
        double oldMaxLeft = oldMonitor.WorkArea.Right - 280 - WindowAnchor.Margin;
        double oldMinTop = WindowAnchor.Margin;
        double oldMaxTop = oldMonitor.WorkArea.Bottom - 220 - WindowAnchor.Margin;
        var widget = new Rect(
            oldMinLeft + ((oldMaxLeft - oldMinLeft) * 0.25),
            oldMinTop + ((oldMaxTop - oldMinTop) * 0.75),
            280,
            220);

        Assert.True(WindowAnchor.TryCaptureIntent(widget, oldMonitor, out WindowPlacementIntent intent));
        Assert.Equal(0.25, intent.XRatio, 6);
        Assert.Equal(0.75, intent.YRatio, 6);
        Assert.True(WindowAnchor.TryComputeFromIntent(280, 220, [newMonitor], newMonitor, intent, out Rect moved));

        double newXRatio = (moved.Left - WindowAnchor.Margin)
            / (newMonitor.WorkArea.Right - 280 - (2 * WindowAnchor.Margin));
        double newYRatio = (moved.Top - WindowAnchor.Margin)
            / (newMonitor.WorkArea.Bottom - 220 - (2 * WindowAnchor.Margin));
        Assert.Equal(0.25, newXRatio, 6);
        Assert.Equal(0.75, newYRatio, 6);
    }

    [Fact]
    public void missing_intent_monitor_uses_primary_with_same_ratios()
    {
        MonitorInfo primary = Primary();
        var intent = new WindowPlacementIntent("DISPLAY-GONE", 1, 0);

        Assert.True(WindowAnchor.TryComputeFromIntent(280, 220, [primary], primary, intent, out Rect rect));

        Assert.Equal(primary.WorkArea.Right - 280 - WindowAnchor.Margin, rect.Left, 6);
        Assert.Equal(primary.WorkArea.Top + WindowAnchor.Margin, rect.Top, 6);
    }

    [Theory]
    [InlineData(double.NaN, 0.5)]
    [InlineData(0.5, double.PositiveInfinity)]
    [InlineData(-0.01, 0.5)]
    [InlineData(0.5, 1.01)]
    public void invalid_intent_is_rejected(double xRatio, double yRatio)
    {
        MonitorInfo primary = Primary();

        bool success = WindowAnchor.TryComputeFromIntent(
            280,
            220,
            [primary],
            primary,
            new WindowPlacementIntent(primary.DeviceName, xRatio, yRatio),
            out _);

        Assert.False(success);
    }

    [Fact]
    public void intent_capture_rejects_widget_larger_than_work_area()
    {
        var tiny = new MonitorInfo("tiny", new Rect(0, 0, 100, 100), 96);

        Assert.False(WindowAnchor.TryCaptureIntent(
            new Rect(8, 8, 280, 220),
            tiny,
            out _));
    }

    private static Rect ComputeWithSettings(
        double widgetWidth,
        double widgetHeight,
        IReadOnlyList<MonitorInfo> monitors,
        MonitorInfo primary,
        AppSettings settings) => WindowAnchor.Compute(
            widgetWidth,
            widgetHeight,
            monitors,
            primary,
            settings.SavedMonitorDeviceName,
            settings.SavedRightEdgeDip,
            settings.SavedTopEdgeDip,
            settings.PlacementMode,
            settings.PlacementAnchor,
            settings.HorizontalMarginDip,
            settings.VerticalMarginDip,
            settings.SavedWorkAreaWidthDip,
            settings.SavedWorkAreaHeightDip,
            settings.PlacementXRatio,
            settings.PlacementYRatio);
}
