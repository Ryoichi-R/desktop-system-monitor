using System;
using System.Collections.Generic;
using System.Linq;
using DesktopSystemMonitor.App.Startup;
using DesktopSystemMonitor.Core.Layout;
using DesktopSystemMonitor.Core.Settings;
using Xunit;

namespace DesktopSystemMonitor.App.Tests;

/// <summary>
/// Regression for the display-connect report: the widget stayed inside the
/// 175% primary work area with a 224x176 px frame while its content was laid
/// out for 224x176 DIP (392x308 px). The fake keeps the native pixel frame,
/// the required DIP size, the window DPI, and the WPF transform independent.
/// </summary>
public sealed class DisplayConnectSizeRegressionTests
{
    private static readonly MonitorInfo Primary = new("\\\\.\\DISPLAY1", new Rect(0, 0, 3840, 2076), 96)
    {
        PhysicalWorkArea = new Rect(0, 0, 3840, 2076),
    };

    private static readonly MonitorInfo Secondary = new("\\\\.\\DISPLAY2", new Rect(-1920, 1084, 1920, 1032), 96)
    {
        PhysicalWorkArea = new Rect(-1920, 1084, 1920, 1032),
    };

    [Theory]
    [InlineData(224d, 168d, 392)]
    [InlineData(176d, 168d, 308)]
    [InlineData(252d, 120d, 315)]
    [InlineData(198d, 120d, 248)]
    [InlineData(252d, 144d, 378)]
    [InlineData(198d, 144d, 297)]
    [InlineData(225d, 168d, 394)]
    [InlineData(198d, 168d, 347)]
    [InlineData(112.5d, 168d, 197)]
    public void expected_pixels_use_one_half_away_from_zero_rounding(double dip, double dpi, int expected)
    {
        Assert.True(WindowPixelSize.TryToDevicePixels(dip, dpi / 96d, out int pixels));
        Assert.Equal(expected, pixels);
    }

    [Theory]
    [InlineData(double.NaN, 1.75)]
    [InlineData(double.PositiveInfinity, 1.75)]
    [InlineData(0d, 1.75)]
    [InlineData(-1d, 1.75)]
    [InlineData(224d, 0d)]
    [InlineData(224d, double.NaN)]
    [InlineData(double.MaxValue, 2d)]
    public void invalid_sizes_and_scales_are_rejected(double dip, double scale)
    {
        Assert.False(WindowPixelSize.TryToDevicePixels(dip, scale, out _));
    }

    [Theory]
    [InlineData(168u, 1.75, 1.75, true)]
    [InlineData(168u, 1.0, 1.0, false)]
    [InlineData(96u, 1.75, 1.75, false)]
    [InlineData(168u, 1.75, 1.0, false)]
    [InlineData(0u, 0.0, 0.0, false)]
    public void dpi_is_confirmed_only_when_window_dpi_and_wpf_transform_agree(uint dpi, double x, double y, bool confirmed)
    {
        Assert.Equal(confirmed, WindowPixelSize.TryGetScale(dpi, x, y, out _));
    }

    [Fact]
    public void stale_frame_inside_the_work_area_was_accepted_without_size_synchronization()
    {
        var surface = new SizingSurface { SynchronizationAvailable = false };
        var state = new State();
        using WindowPlacementController controller = Create(surface, state);

        Assert.True(controller.PositionWindow());

        Assert.Equal(new Rect(3602, 14, 224, 176), surface.Bounds);
        Assert.NotEqual(392, surface.NativeWidth);
    }

    [Fact]
    public void stale_frame_is_resized_to_the_required_dip_size_before_preset_placement()
    {
        var surface = new SizingSurface();
        var state = new State();
        using WindowPlacementController controller = Create(surface, state);

        Assert.True(controller.PositionWindow());

        Assert.Equal(new Rect(3434, 14, 392, 308), surface.Bounds);
        Assert.Equal(1, surface.NativeResizeCount);
        Assert.Equal(0, state.Saves);
        Assert.Contains("placement-size-sync", state.Diagnostics);
    }

    [Fact]
    public void dpi_round_trips_and_repeated_notifications_do_not_accumulate_size()
    {
        var surface = new SizingSurface();
        var state = new State();
        using WindowPlacementController controller = Create(surface, state);

        foreach (double dpi in new double[] { 168, 96, 168, 168, 96, 96, 168 })
        {
            surface.ChangeDpi(dpi, keepNativeSize: true);
            surface.RaiseBoundsChanged();
            controller.CompletePendingDisplayReflow();

            int expectedWidth = (int)Math.Round(224 * dpi / 96, MidpointRounding.AwayFromZero);
            int expectedHeight = (int)Math.Round(176 * dpi / 96, MidpointRounding.AwayFromZero);
            Assert.Equal(expectedWidth, surface.NativeWidth);
            Assert.Equal(expectedHeight, surface.NativeHeight);
            Assert.Equal(3840 - Math.Round(8 * dpi / 96, 6) - expectedWidth, surface.NativeLeft, 6);
            Assert.False(controller.ReflowPending);
        }
    }

    [Fact]
    public void unconfirmed_dpi_fails_after_three_attempts_without_saving_and_recovers_on_next_notification()
    {
        var surface = new SizingSurface { WpfScale = 1.0 };
        var state = new State();
        using WindowPlacementController controller = Create(surface, state);

        Assert.False(controller.PositionWindow());
        controller.PersistWindowPosition();
        Assert.Equal(0, state.Saves);
        Assert.Contains("placement-size-sync-failure", state.Diagnostics);
        Assert.Contains("placement-capture-size-mismatch", state.Diagnostics);
        Assert.Equal(0, surface.MoveCount);
        Assert.Equal(0, surface.NativeResizeCount);

        surface.WpfScale = 1.75;
        surface.RaiseBoundsChanged();
        Assert.True(controller.ReflowPending);
        controller.CompletePendingDisplayReflow();

        Assert.Equal(new Rect(3434, 14, 392, 308), surface.Bounds);
        Assert.False(controller.ReflowPending);
    }

    [Fact]
    public void ineffective_native_resize_is_a_failure_and_is_not_saved()
    {
        var surface = new SizingSurface { IgnoreNativeResize = true };
        var state = new State();
        using WindowPlacementController controller = Create(surface, state);

        Assert.False(controller.PositionWindow());

        Assert.Equal(3, surface.NativeResizeCount);
        Assert.Equal(0, surface.MoveCount);
        Assert.Contains("placement-size-sync-failure", state.Diagnostics);
        Assert.DoesNotContain("placement-verification-failure", state.Diagnostics);
        Assert.Equal(0, state.Saves);
    }

    [Fact]
    public void verified_bounds_echo_does_not_start_a_new_reflow_wave()
    {
        var surface = new SizingSurface();
        var state = new State();
        using WindowPlacementController controller = Create(surface, state);
        Assert.True(controller.PositionWindow());

        surface.RaiseBoundsChanged();
        Assert.False(controller.ReflowPending);
        Assert.False(state.ReflowTimer.IsEnabled);

        surface.SimulateStaleDisplayConnect();
        surface.RaiseBoundsChanged();
        Assert.True(controller.ReflowPending);
    }

    [Fact]
    public void notifications_raised_during_placement_are_echoes_of_the_current_wave()
    {
        var surface = new SizingSurface();
        var state = new State();
        using WindowPlacementController controller = Create(surface, state);
        surface.AfterResize = surface.RaiseBoundsChanged;
        surface.AfterMove = surface.RaiseBoundsChanged;

        Assert.True(controller.PositionWindow());

        Assert.False(controller.ReflowPending);
        Assert.False(state.ReflowTimer.IsEnabled);
    }

    [Fact]
    public void display_connect_wave_with_dpi_then_size_then_display_order_converges_once()
    {
        var surface = new SizingSurface();
        var state = new State();
        using WindowPlacementController controller = Create(surface, state);
        Assert.True(controller.PositionWindow());

        surface.ChangeDpi(96, keepNativeSize: false);
        surface.RaiseBoundsChanged();
        surface.ChangeDpi(168, keepNativeSize: true);
        surface.RaiseBoundsChanged();
        state.Layout = new MonitorLayout(new[] { Primary, Secondary }, Primary);
        surface.RaiseDisplayConfigurationChanged();
        Assert.True(controller.ReflowPending);
        controller.CompletePendingDisplayReflow();

        Assert.Equal(new Rect(3434, 14, 392, 308), surface.Bounds);
        Assert.False(controller.ReflowPending);
        Assert.False(state.ReflowTimer.IsEnabled);
    }

    [Fact]
    public void reflow_waits_for_user_drag_before_resizing()
    {
        var surface = new SizingSurface();
        var state = new State();
        using WindowPlacementController controller = Create(surface, state);
        surface.RaiseUserMoveStarted();
        surface.RaiseBoundsChanged();

        controller.CompletePendingDisplayReflow();
        Assert.Equal(0, surface.NativeResizeCount);
        Assert.True(controller.ReflowPending);

        surface.RaiseUserMoveCompleted();
        controller.CompletePendingDisplayReflow();
        Assert.Equal(1, surface.NativeResizeCount);
        Assert.Equal(392, surface.NativeWidth);
    }

    [Fact]
    public void disposed_controller_ignores_size_notifications()
    {
        var surface = new SizingSurface();
        var state = new State();
        WindowPlacementController controller = Create(surface, state);
        controller.Dispose();

        surface.RaiseBoundsChanged();
        controller.CompletePendingDisplayReflow();

        Assert.Equal(0, surface.NativeResizeCount);
        Assert.False(controller.PositionWindow());
        Assert.Equal(0, state.Saves);
    }

    [Fact]
    public void moving_to_a_100_percent_monitor_resynchronizes_at_the_destination_dpi()
    {
        var surface = new SizingSurface();
        var state = new State { Layout = new MonitorLayout(new[] { Primary, Secondary }, Primary) };
        state.Settings = state.Settings with { SavedMonitorDeviceName = Secondary.DeviceName };
        surface.AfterMove = () =>
        {
            if (surface.NativeLeft < 0 && surface.Dpi != 96)
            {
                // The OS moves the frame and sends WM_DPICHANGED with a
                // rectangle that keeps the 175% pixel size.
                surface.ChangeDpi(96, keepNativeSize: true);
            }
        };
        using WindowPlacementController controller = Create(surface, state);

        Assert.True(controller.PositionWindow());

        Assert.Equal(96, surface.Dpi);
        Assert.Equal(224, surface.NativeWidth);
        Assert.Equal(176, surface.NativeHeight);
        Assert.Equal(-8 - 224, surface.NativeLeft);
        Assert.Equal(1084 + 8, surface.NativeTop);
    }

    [Fact]
    public void disconnected_saved_monitor_falls_back_to_primary_with_synchronized_size()
    {
        var surface = new SizingSurface();
        var state = new State();
        state.Settings = state.Settings with { SavedMonitorDeviceName = "\\\\.\\DISPLAY9", PlacementAnchor = WindowPlacementAnchor.BottomLeft };
        using WindowPlacementController controller = Create(surface, state);

        Assert.True(controller.PositionWindow());

        Assert.Equal(new Rect(14, 2076 - 14 - 308, 392, 308), surface.Bounds);
    }

    [Fact]
    public void preview_cancel_after_connect_restores_with_the_synchronized_size_without_saving()
    {
        var surface = new SizingSurface();
        var state = new State();
        using WindowPlacementController controller = Create(surface, state);
        Assert.True(controller.PositionWindow());
        controller.BeginPlacementPreviewSession();
        controller.PreviewPlacement(state.Settings with { PlacementAnchor = WindowPlacementAnchor.BottomLeft });

        surface.SimulateStaleDisplayConnect();
        surface.RaiseBoundsChanged();
        controller.CompletePendingDisplayReflow();
        Assert.Equal(392, surface.NativeWidth);
        Assert.Equal(14, surface.NativeLeft);

        surface.SimulateStaleDisplayConnect();
        Assert.True(controller.CancelPlacementPreviewSession());

        Assert.Equal(new Rect(3434, 14, 392, 308), surface.Bounds);
        Assert.Equal(0, state.Saves);
        Assert.False(controller.PlacementPreviewActive);
    }

    [Fact]
    public void custom_intent_is_not_recaptured_from_the_stale_frame()
    {
        var surface = new SizingSurface();
        var state = new State();
        state.Settings = state.Settings with
        {
            PlacementMode = WindowPlacementMode.Custom,
            SavedMonitorDeviceName = Primary.DeviceName,
            SavedMonitorDpi = 168,
            PlacementXRatio = 0.5,
            PlacementYRatio = 0.25,
            SavedWorkAreaWidthDip = 3840 / 1.75,
            SavedWorkAreaHeightDip = 2076 / 1.75,
            SavedRightEdgeDip = 1200,
            SavedTopEdgeDip = 300,
        };
        using WindowPlacementController controller = Create(surface, state);

        Assert.True(controller.PositionWindow());
        Rect placed = surface.Bounds;
        Assert.Equal(392, placed.Width);

        surface.SimulateStaleDisplayConnect();
        surface.RaiseUserMoveStarted();
        surface.RaiseUserMoveCompleted();
        controller.FlushPendingWindowPosition();
        Assert.Equal(0, state.Saves);
        Assert.Contains("placement-capture-size-mismatch", state.Diagnostics);
        Assert.True(controller.ReflowPending);

        controller.CompletePendingDisplayReflow();

        Assert.Equal(placed, surface.Bounds);
        Assert.Equal(1, state.Saves);
        Assert.Equal((placed.Left - 8) / (3840 - 392 - 16), state.Settings.PlacementXRatio!.Value, 6);
        Assert.Equal((placed.Top - 8) / (2076 - 308 - 16), state.Settings.PlacementYRatio!.Value, 6);
        Assert.Equal(placed.Right / 1.75, state.Settings.SavedRightEdgeDip!.Value, 6);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void current_position_capture_preserves_intent_until_stale_size_is_repaired(bool previewActive)
    {
        var surface = new SizingSurface();
        var state = new State();
        using WindowPlacementController controller = Create(surface, state);
        Assert.True(controller.PositionWindow());
        AppSettings confirmed = controller.CaptureCurrentWindowPosition(state.Settings);
        if (previewActive)
        {
            controller.BeginPlacementPreviewSession();
        }

        surface.SimulateStaleDisplayConnect();
        Assert.False(surface.CheckSize().IsSynchronized);
        AppSettings captured = controller.CaptureCurrentWindowPosition(confirmed);

        Assert.Same(confirmed, captured);
        Assert.Equal(0, state.Saves);
        Assert.True(controller.ReflowPending);
        Assert.Contains("placement-capture-size-mismatch", state.Diagnostics);
        controller.CompletePendingDisplayReflow();
        Assert.True(surface.CheckSize().IsSynchronized);
        AppSettings repaired = controller.CaptureCurrentWindowPosition(confirmed);
        Assert.Equal(confirmed.PlacementXRatio, repaired.PlacementXRatio);
        Assert.Equal(confirmed.SavedRightEdgeDip, repaired.SavedRightEdgeDip);
        Assert.Equal(previewActive ? 0 : 1, state.Saves);
    }

    [Theory]
    [InlineData((int)WindowSizeSyncStatus.DpiNotConfirmed)]
    [InlineData((int)WindowSizeSyncStatus.NativeBoundsUnavailable)]
    [InlineData((int)WindowSizeSyncStatus.NotReady)]
    public void current_position_capture_does_not_trust_unconfirmed_or_synthesized_bounds(int status)
    {
        var surface = new SizingSurface { ReadFailure = (WindowSizeSyncStatus)status };
        var state = new State();
        using WindowPlacementController controller = Create(surface, state);

        Assert.Same(state.Settings, controller.CaptureCurrentWindowPosition(state.Settings));
        Assert.Equal(0, state.Saves);
        Assert.True(controller.ReflowPending);
    }

    [Fact]
    public void current_position_capture_after_dispose_keeps_the_basis_and_does_not_schedule_work()
    {
        var surface = new SizingSurface();
        var state = new State();
        WindowPlacementController controller = Create(surface, state);
        controller.Dispose();

        Assert.Same(state.Settings, controller.CaptureCurrentWindowPosition(state.Settings));
        Assert.False(controller.ReflowPending);
        Assert.Equal(0, state.Saves);
    }

    [Fact]
    public void monitor_diagnostics_keep_measured_dpi_separate_from_the_placement_basis()
    {
        MonitorInfo monitor = Secondary with
        {
            PhysicalBounds = new Rect(-1920, 1084, 1920, 1080),
            EffectiveDpi = 168,
        };

        string description = WindowPlacementController.DescribeMonitor(monitor);

        Assert.Contains("physicalBounds=(-1920,1084,1920,1080)", description);
        Assert.Contains("physicalWorkArea=(-1920,1084,1920,1032)", description);
        Assert.Contains("effectiveDpi=168", description);
        Assert.Contains("placementDpi=96", description);
        Assert.Equal(96, monitor.Dpi);
        Assert.Contains("effectiveDpi=unavailable", WindowPlacementController.DescribeMonitor(Secondary));
        Assert.Contains("physicalBounds=unavailable", WindowPlacementController.DescribeMonitor(Secondary));
    }

    [Fact]
    public void every_monitor_has_a_separate_bounded_diagnostic_entry()
    {
        var surface = new SizingSurface();
        var state = new State();
        MonitorInfo[] monitors = Enumerable.Range(0, 8).Select(index => Primary with
        {
            DeviceName = "DISPLAY" + index,
            PhysicalBounds = new Rect(index * 3840, 0, 3840, 2160),
            PhysicalWorkArea = new Rect(index * 3840, 0, 3840, 2076),
            EffectiveDpi = index == 0 ? 168 : 96,
        }).ToArray();
        state.Layout = new MonitorLayout(monitors, monitors[0]);
        using WindowPlacementController controller = Create(surface, state);

        Assert.True(controller.PositionWindow());

        string[] entries = state.Details.Where(item => item.Category == "placement-monitor").Select(item => item.Message).ToArray();
        Assert.Equal(8, entries.Length);
        foreach (MonitorInfo monitor in monitors)
        {
            Assert.Contains(entries, entry => entry.StartsWith("device=" + monitor.DeviceName + ";", StringComparison.Ordinal));
        }
        Assert.All(entries, entry => Assert.True(entry.Length < Diagnostics.PlacementDiagnosticDetail.MaximumLength));
    }

    private static WindowPlacementController Create(SizingSurface surface, State state) =>
        new(surface, () => state.Settings, update => { state.Settings = update(state.Settings); state.Saves++; },
            () => state.Layout, () => DateTimeOffset.UtcNow,
            (category, detail) => { state.Diagnostics.Add(category); state.Details.Add((category, detail?.Message ?? string.Empty)); }, state.PersistTimer, state.ReflowTimer);

    private sealed class State
    {
        public AppSettings Settings = new AppSettings
        {
            PlacementMode = WindowPlacementMode.Preset,
            PlacementAnchor = WindowPlacementAnchor.TopRight,
            HorizontalMarginDip = 8,
            VerticalMarginDip = 8,
            UiScalePercent = 80,
        }.Normalized();
        public int Saves;
        public List<string> Diagnostics = new();
        public List<(string Category, string Message)> Details = new();
        public Timer PersistTimer = new();
        public Timer ReflowTimer = new();
        public MonitorLayout Layout = new(new[] { Primary }, Primary);
    }

    /// <summary>
    /// Native frame in physical pixels, required content in DIP, window DPI
    /// from GetDpiForWindow, and the WPF device transform are separate values.
    /// Synchronization follows the product contract: it succeeds only at a
    /// confirmed DPI and resizes the native frame to the rounded pixel size.
    /// </summary>
    private sealed class SizingSurface : IWindowPlacementSurface
    {
        public double NativeLeft = 3602;
        public double NativeTop = 14;
        public double NativeWidth = 224;
        public double NativeHeight = 176;
        public double RequiredWidthDip = 224;
        public double RequiredHeightDip = 176;
        public double Dpi = 168;
        public double WpfScale = 1.75;
        public bool SynchronizationAvailable = true;
        public bool IgnoreNativeResize;
        public WindowSizeSyncStatus? ReadFailure;
        public int NativeResizeCount;
        public int MoveCount;
        public Action AfterMove;
        public Action AfterResize;

        public Rect Bounds => new(NativeLeft, NativeTop, NativeWidth, NativeHeight);
        public double? CurrentDpi => Dpi;
        public bool IsLoaded => true;

        public void ChangeDpi(double dpi, bool keepNativeSize)
        {
            Dpi = dpi;
            WpfScale = dpi / 96;
            if (!keepNativeSize)
            {
                NativeWidth = Math.Round(RequiredWidthDip * WpfScale, MidpointRounding.AwayFromZero);
                NativeHeight = Math.Round(RequiredHeightDip * WpfScale, MidpointRounding.AwayFromZero);
            }
        }

        /// <summary>
        /// Display connect on the affected PC: the frame follows a transient
        /// 100% DPI, then the return to 175% keeps the 100% pixel size.
        /// </summary>
        public void SimulateStaleDisplayConnect()
        {
            ChangeDpi(96, keepNativeSize: false);
            ChangeDpi(168, keepNativeSize: true);
        }

        public WindowSizeSyncResult CheckSize() => Evaluate(apply: false);

        public WindowSizeSyncResult SynchronizeSize() => Evaluate(apply: true);

        private WindowSizeSyncResult Evaluate(bool apply)
        {
            if (ReadFailure is WindowSizeSyncStatus failure)
            {
                return new WindowSizeSyncResult(failure, "fake");
            }
            if (!SynchronizationAvailable)
            {
                return WindowSizeSyncResult.Unchecked;
            }
            if (!WindowPixelSize.TryGetScale((uint)Dpi, WpfScale, WpfScale, out double scale))
            {
                return new WindowSizeSyncResult(WindowSizeSyncStatus.DpiNotConfirmed, "fake");
            }
            Assert.True(WindowPixelSize.TryToDevicePixels(RequiredWidthDip, scale, out int width));
            Assert.True(WindowPixelSize.TryToDevicePixels(RequiredHeightDip, scale, out int height));
            if (WindowPixelSize.Matches(NativeWidth, width) && WindowPixelSize.Matches(NativeHeight, height))
            {
                return new WindowSizeSyncResult(WindowSizeSyncStatus.Matched, "fake");
            }
            if (!apply)
            {
                return new WindowSizeSyncResult(WindowSizeSyncStatus.Mismatch, "fake");
            }
            NativeResizeCount++;
            if (!IgnoreNativeResize)
            {
                NativeWidth = width;
                NativeHeight = height;
            }
            AfterResize?.Invoke();
            return IgnoreNativeResize
                ? new WindowSizeSyncResult(WindowSizeSyncStatus.Mismatch, "fake")
                : new WindowSizeSyncResult(WindowSizeSyncStatus.Corrected, "fake");
        }

        public bool TrySetPosition(double left, double top)
        {
            MoveCount++;
            NativeLeft = left;
            NativeTop = top;
            AfterMove?.Invoke();
            return true;
        }

        public event Action LocationChanged { add { } remove { } }
        public event Action Closing { add { } remove { } }
        public event Action DisplayConfigurationChanged;
        public event Action BoundsChanged;
        public event Action UserMoveStarted;
        public event Action UserMoveCompleted;
        public void RaiseBoundsChanged() => BoundsChanged?.Invoke();
        public void RaiseDisplayConfigurationChanged() => DisplayConfigurationChanged?.Invoke();
        public void RaiseUserMoveStarted() => UserMoveStarted?.Invoke();
        public void RaiseUserMoveCompleted() => UserMoveCompleted?.Invoke();
    }

    private sealed class Timer : IPlacementTimer
    {
        public bool IsEnabled { get; private set; }
        public TimeSpan Interval { get; set; }
        public void Start() => IsEnabled = true;
        public void Stop() => IsEnabled = false;
    }
}
