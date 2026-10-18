using System;
using System.Collections.Generic;
using DesktopSystemMonitor.App.Startup;
using DesktopSystemMonitor.Core.Layout;
using DesktopSystemMonitor.Core.Settings;
using Xunit;

namespace DesktopSystemMonitor.App.Tests;

public sealed class PlacementPreviewRegressionTests
{
    [Fact]
    public void cancel_after_size_increase_clamps_current_bounds_without_saving()
    {
        var surface = new Surface { Left = 1688, Width = 224 };
        var state = new State();
        using var controller = Create(surface, state);
        controller.BeginPlacementPreviewSession();
        controller.PreviewPlacement(state.Settings with { PlacementAnchor = WindowPlacementAnchor.TopLeft });
        surface.Width = 392;
        surface.RaiseBoundsChanged();
        controller.CompletePendingDisplayReflow();

        Assert.True(controller.CancelPlacementPreviewSession());

        Assert.True(surface.Bounds.Right <= 1920);
        Assert.Equal(0, state.Saves);
        Assert.False(controller.PlacementPreviewActive);
        Assert.False(state.PersistTimer.IsEnabled);
        Assert.False(state.ReflowTimer.IsEnabled);
    }

    [Fact]
    public void ineffective_native_success_is_rejected_after_three_attempts()
    {
        var surface = new Surface { Left = 1688, IgnoreMove = true };
        var state = new State();
        using var controller = Create(surface, state);

        Assert.False(controller.PositionWindow());
        Assert.Equal(3, surface.MoveCount);
        Assert.Contains("placement-verification-failure", state.Diagnostics);
        Assert.Equal(0, state.Saves);
    }

    [Fact]
    public void move_failure_does_not_persist_or_release_the_preview()
    {
        var surface = new Surface { MoveSucceeds = false };
        var state = new State();
        using var controller = Create(surface, state);
        controller.BeginPlacementPreviewSession();
        int persisted = 0;

        Assert.False(controller.CommitPlacementPreview(state.Settings, () => { persisted++; return true; }));
        Assert.Equal(0, persisted);
        Assert.True(controller.PlacementPreviewActive);
        controller.ApplySettingsLayoutChange();
        controller.FlushBeforeShutdown();
        Assert.Equal(0, state.Saves);
    }

    [Fact]
    public void synchronous_size_and_dpi_change_is_recomputed_before_success()
    {
        var surface = new Surface { Width = 224, CurrentDpi = 96 };
        surface.AfterMove = () => { surface.Width = 392; surface.CurrentDpi = 168; };
        var state = new State();
        using var controller = Create(surface, state);

        Assert.True(controller.PositionWindow());
        Assert.Equal(1514, surface.Left);
        Assert.Equal(14, surface.Top);
        Assert.Equal(2, surface.MoveCount);
        Assert.Equal(0, state.Saves);
    }

    [Fact]
    public void failed_save_retains_preview_and_retry_commits_final_draft()
    {
        var surface = new Surface();
        var state = new State();
        using var controller = Create(surface, state);
        controller.PreviewPlacement(state.Settings with { PlacementAnchor = WindowPlacementAnchor.TopLeft });
        int attempts = 0;
        AppSettings final = state.Settings with { PlacementAnchor = WindowPlacementAnchor.BottomRight };

        Assert.False(controller.CommitPlacementPreview(final, () => { attempts++; return false; }));
        Assert.True(controller.PlacementPreviewActive);
        surface.RaiseLocationChanged();
        controller.FlushPendingWindowPosition();
        Assert.Equal(0, state.Saves);
        Assert.True(controller.CommitPlacementPreview(final, () => { attempts++; return true; }));
        Assert.Equal(2, attempts);
        Assert.False(controller.PlacementPreviewActive);
        Assert.Equal(1520, surface.Left);
        Assert.Equal(716, surface.Top);
    }

    [Fact]
    public void cancel_enumeration_failure_discards_preview_and_stops_timers()
    {
        var surface = new Surface();
        var state = new State();
        bool fail = false;
        using var controller = Create(surface, state, () => fail ? throw new InvalidOperationException("injected") : state.Layout);
        controller.PreviewPlacement(state.Settings);
        surface.RaiseBoundsChanged();
        fail = true;

        Assert.False(controller.CancelPlacementPreviewSession());
        Assert.False(controller.PlacementPreviewActive);
        Assert.False(controller.ReflowPending);
        Assert.False(state.PersistTimer.IsEnabled);
        Assert.False(state.ReflowTimer.IsEnabled);
        Assert.Contains("placement-preview-cancel-failure", state.Diagnostics);
        Assert.Equal(0, state.Saves);
    }

    [Fact]
    public void cancel_and_diagnostic_failure_still_release_the_settings_flow_lease()
    {
        var surface = new Surface();
        var state = new State();
        bool fail = false;
        using var controller = Create(surface, state,
            () => fail ? throw new InvalidOperationException("enumeration") : state.Layout,
            (_, _) => { if (fail) throw new InvalidOperationException("diagnostic"); });
        controller.PreviewPlacement(state.Settings);
        fail = true;
        int released = 0;
        int reapplied = 0;
        using var completion = new SettingsFlowCompletionScope(
            () => controller.CancelPlacementPreviewSession(),
            () => released++,
            () => reapplied++,
            _ => { });

        completion.Dispose();

        Assert.Equal(1, released);
        Assert.Equal(1, reapplied);
        Assert.False(controller.PlacementPreviewActive);
        Assert.Equal(0, state.Saves);
    }

    [Fact]
    public void disconnected_preview_uses_primary_without_overwriting_the_draft_or_persisted_settings()
    {
        var surface = new Surface();
        var state = new State();
        AppSettings draft = state.Settings with { SavedMonitorDeviceName = "DISCONNECTED", PlacementAnchor = WindowPlacementAnchor.BottomLeft };
        using var controller = Create(surface, state);

        string status = controller.PreviewPlacement(draft);
        surface.RaiseBoundsChanged();
        controller.CompletePendingDisplayReflow();

        Assert.Contains("未接続", status, StringComparison.Ordinal);
        Assert.Equal("DISCONNECTED", draft.SavedMonitorDeviceName);
        Assert.Null(state.Settings.SavedMonitorDeviceName);
        Assert.Equal(8, surface.Left);
        Assert.Equal(716, surface.Top);
        Assert.Equal(0, state.Saves);
    }

    [Fact]
    public void oversized_widget_warns_and_ineffective_move_does_not_misreport_size_failure()
    {
        var state = new State();
        var surface = new Surface { Width = 2000 };
        using var controller = Create(surface, state);
        Assert.Contains("全体は収まりません", controller.PreviewPlacement(state.Settings), StringComparison.Ordinal);
        surface.Width = 392;
        surface.Left = 1688;
        surface.IgnoreMove = true;
        string failed = controller.PreviewPlacement(state.Settings);
        Assert.Contains("適用できません", failed, StringComparison.Ordinal);
        Assert.DoesNotContain("ウィジェットより小さい", failed, StringComparison.Ordinal);
    }

    [Fact]
    public void not_ready_size_waits_for_bounds_notification_without_saving_invalid_values()
    {
        var surface = new Surface { Width = 0 };
        var state = new State();
        using var controller = Create(surface, state);
        Assert.False(controller.PositionWindow());
        controller.PersistWindowPosition();
        Assert.Equal(0, state.Saves);
        surface.Width = 392;
        surface.RaiseBoundsChanged();
        controller.CompletePendingDisplayReflow();
        Assert.Equal(1520, surface.Left);
        Assert.False(controller.ReflowPending);
    }

    public static IEnumerable<object[]> PlacementCases()
    {
        foreach (double dpi in new double[] { 96, 120, 144, 168, 192 })
            foreach (int scale in new[] { 75, 100, 140, 200 })
                foreach (WindowPlacementAnchor anchor in Enum.GetValues<WindowPlacementAnchor>())
                    foreach (bool expanded in new[] { false, true })
                        yield return new object[] { dpi, scale, anchor, expanded };
    }

    [Theory]
    [MemberData(nameof(PlacementCases))]
    public void all_anchors_fit_negative_origins_at_each_dpi_scale_and_background_size(double dpi, int scale, WindowPlacementAnchor anchor, bool expanded)
    {
        double factor = dpi / 96 * scale / 100d;
        var surface = new Surface { Width = (224 + (expanded ? 100 : 0)) * factor, Height = 180 * factor, CurrentDpi = dpi };
        var monitor = new MonitorInfo("DISPLAY1", new Rect(-1920, -1080, 1920, 1032), dpi);
        var state = new State { Layout = new MonitorLayout(new[] { monitor }, monitor) };
        using var controller = Create(surface, state);
        controller.PreviewPlacement(state.Settings with { PlacementAnchor = anchor });

        Assert.True(surface.Bounds.Left >= monitor.WorkArea.Left);
        Assert.True(surface.Bounds.Top >= monitor.WorkArea.Top);
        Assert.True(surface.Bounds.Right <= monitor.WorkArea.Right);
        Assert.True(surface.Bounds.Bottom <= monitor.WorkArea.Bottom);
        double margin = 8 * dpi / 96;
        bool right = anchor is WindowPlacementAnchor.TopRight or WindowPlacementAnchor.BottomRight;
        bool bottom = anchor is WindowPlacementAnchor.BottomLeft or WindowPlacementAnchor.BottomRight;
        Assert.Equal(right ? monitor.WorkArea.Right - margin : monitor.WorkArea.Left + margin, right ? surface.Bounds.Right : surface.Bounds.Left, 6);
        Assert.Equal(bottom ? monitor.WorkArea.Bottom - margin : monitor.WorkArea.Top + margin, bottom ? surface.Bounds.Bottom : surface.Bounds.Top, 6);
        Assert.Equal(0, state.Saves);
    }

    private static WindowPlacementController Create(Surface surface, State state, Func<MonitorLayout> enumerate = null, Action<string, Exception> diagnose = null) =>
        new(surface, () => state.Settings, update => { state.Settings = update(state.Settings); state.Saves++; },
            enumerate ?? (() => state.Layout), () => DateTimeOffset.UtcNow,
            diagnose ?? ((category, _) => state.Diagnostics.Add(category)), state.PersistTimer, state.ReflowTimer);

    private sealed class State
    {
        public AppSettings Settings = new AppSettings().Normalized();
        public int Saves;
        public List<string> Diagnostics = new();
        public Timer PersistTimer = new();
        public Timer ReflowTimer = new();
        public MonitorLayout Layout = DefaultLayout();
        private static MonitorLayout DefaultLayout()
        {
            var monitor = new MonitorInfo("DISPLAY1", new Rect(0, 0, 1920, 1032), 96);
            return new MonitorLayout(new[] { monitor }, monitor);
        }
    }

    private sealed class Surface : IWindowPlacementSurface
    {
        public double Left;
        public double Top = 8;
        public double Width = 392;
        public double Height = 308;
        public Rect Bounds => new(Left, Top, Width, Height);
        public double? CurrentDpi { get; set; }
        public bool IsLoaded => true;
        public bool MoveSucceeds = true;
        public bool IgnoreMove;
        public int MoveCount;
        public Action AfterMove;
        public bool TrySetPosition(double left, double top)
        {
            MoveCount++;
            if (MoveSucceeds && !IgnoreMove) { Left = left; Top = top; }
            AfterMove?.Invoke();
            return MoveSucceeds;
        }
        public event Action LocationChanged;
        public event Action Closing { add { } remove { } }
        public event Action DisplayConfigurationChanged { add { } remove { } }
        public event Action BoundsChanged;
        public event Action UserMoveStarted { add { } remove { } }
        public event Action UserMoveCompleted { add { } remove { } }
        public void RaiseBoundsChanged() => BoundsChanged?.Invoke();
        public void RaiseLocationChanged() => LocationChanged?.Invoke();
    }

    private sealed class Timer : IPlacementTimer
    {
        public bool IsEnabled { get; private set; }
        public TimeSpan Interval { get; set; }
        public void Start() => IsEnabled = true;
        public void Stop() => IsEnabled = false;
    }
}
