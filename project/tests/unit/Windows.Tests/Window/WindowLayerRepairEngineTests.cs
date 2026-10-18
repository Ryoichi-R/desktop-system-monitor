using System.Runtime.InteropServices;
using DesktopSystemMonitor.Windows.Window;
using Xunit;

namespace DesktopSystemMonitor.Windows.Tests.Window;

public sealed class WindowLayerRepairEngineTests
{
    [Fact]
    public void native_adapter_clears_stale_error_before_accepting_zero_style()
    {
        Marshal.SetLastPInvokeError(123);
        var api = new NativeWindowLayerApi(
            (_, _) => IntPtr.Zero,
            (_, _, _, _, _, _, _) => true);

        WindowStyleObservation result = api.GetExtendedStyle(new IntPtr(10));

        Assert.True(result.Succeeded);
        Assert.False(result.IsTopMost);
        Assert.Equal(0, result.ErrorCode);
    }

    [Fact]
    public void native_adapter_distinguishes_zero_style_from_a_real_read_error()
    {
        var api = new NativeWindowLayerApi(
            (_, _) =>
            {
                Marshal.SetLastPInvokeError(6);
                return IntPtr.Zero;
            },
            (_, _, _, _, _, _, _) => true);

        WindowStyleObservation result = api.GetExtendedStyle(new IntPtr(10));

        Assert.False(result.Succeeded);
        Assert.Equal(6, result.ErrorCode);
    }

    [Fact]
    public void native_adapter_captures_set_window_pos_error_immediately()
    {
        var api = new NativeWindowLayerApi(
            (_, _) => IntPtr.Zero,
            (_, _, _, _, _, _, _) =>
            {
                Marshal.SetLastPInvokeError(5);
                return false;
            });

        WindowPositionCallResult result = api.SetWindowPosition(
            new IntPtr(10),
            WindowInterop.HWND_TOPMOST,
            WindowInterop.SWP_NOACTIVATE);

        Assert.False(result.Succeeded);
        Assert.Equal(5, result.ErrorCode);
    }

    [Fact]
    public void healthy_topmost_is_observed_without_reapplying()
    {
        var api = new FakeWindowLayerApi { IsTopMost = true };
        var engine = new WindowLayerRepairEngine(api);

        LayerRepairResult result = engine.Repair(
            new IntPtr(10),
            LayerStrategy.TopMost,
            LayerRepairTrigger.Timer,
            countBottomMostFailure: true);

        Assert.Equal(LayerRepairOutcome.Healthy, result.Outcome);
        Assert.Empty(api.SetCalls);
        Assert.Equal(1, api.GetStyleCalls);
    }

    [Fact]
    public void topmost_mismatch_is_repaired_with_expected_flags_and_diagnostics()
    {
        var api = new FakeWindowLayerApi { IsTopMost = false };
        var diagnostics = new List<LayerDiagnosticEvent>();
        var engine = new WindowLayerRepairEngine(api, diagnostics.Add);

        LayerRepairResult result = engine.Repair(
            new IntPtr(10),
            LayerStrategy.TopMost,
            LayerRepairTrigger.Timer,
            countBottomMostFailure: true);

        Assert.Equal(LayerRepairOutcome.Healthy, result.Outcome);
        WindowSetCall call = Assert.Single(api.SetCalls);
        Assert.Equal(WindowInterop.HWND_TOPMOST, call.InsertAfter);
        Assert.Equal(
            WindowInterop.SWP_NOMOVE | WindowInterop.SWP_NOSIZE | WindowInterop.SWP_NOACTIVATE,
            call.Flags);
        Assert.Equal(
            [LayerDiagnosticCategory.RepairEnter, LayerDiagnosticCategory.RepairRecovered],
            diagnostics.Select(item => item.Category));
    }

    [Fact]
    public void normal_mode_removes_unexpected_topmost_state()
    {
        var api = new FakeWindowLayerApi { IsTopMost = true };
        var engine = new WindowLayerRepairEngine(api);

        LayerRepairResult result = engine.Repair(
            new IntPtr(10),
            LayerStrategy.Normal,
            LayerRepairTrigger.Timer,
            countBottomMostFailure: true);

        Assert.Equal(LayerRepairOutcome.Healthy, result.Outcome);
        Assert.Equal(WindowInterop.HWND_NOTOPMOST, Assert.Single(api.SetCalls).InsertAfter);
        Assert.False(api.IsTopMost);
    }

    [Fact]
    public void forced_normal_transition_applies_even_when_topmost_style_already_matches()
    {
        var api = new FakeWindowLayerApi { IsTopMost = false };
        var engine = new WindowLayerRepairEngine(api);

        LayerRepairResult result = engine.Repair(
            new IntPtr(10),
            LayerStrategy.Normal,
            LayerRepairTrigger.LayerSetter,
            countBottomMostFailure: false,
            forceApply: true);

        Assert.Equal(LayerRepairOutcome.Healthy, result.Outcome);
        Assert.Equal(WindowInterop.HWND_NOTOPMOST, Assert.Single(api.SetCalls).InsertAfter);
    }

    [Fact]
    public void failed_forced_normal_transition_is_retried_despite_matching_style()
    {
        var api = new FakeWindowLayerApi { IsTopMost = false, SetErrorCode = 5 };
        var engine = new WindowLayerRepairEngine(api);

        LayerRepairResult failed = engine.Repair(
            new IntPtr(10),
            LayerStrategy.Normal,
            LayerRepairTrigger.LayerSetter,
            countBottomMostFailure: false,
            forceApply: true);
        api.SetErrorCode = 0;
        api.SetCalls.Clear();
        LayerRepairResult recovered = engine.Repair(
            new IntPtr(10),
            LayerStrategy.Normal,
            LayerRepairTrigger.Timer,
            countBottomMostFailure: true);

        Assert.Equal(LayerRepairOutcome.Failed, failed.Outcome);
        Assert.Equal(LayerRepairOutcome.Healthy, recovered.Outcome);
        Assert.Equal(WindowInterop.HWND_NOTOPMOST, Assert.Single(api.SetCalls).InsertAfter);
    }

    [Fact]
    public void observation_episode_reset_preserves_failed_forced_transition()
    {
        var api = new FakeWindowLayerApi { IsTopMost = false, SetErrorCode = 5 };
        var engine = new WindowLayerRepairEngine(api);
        _ = engine.Repair(
            new IntPtr(10),
            LayerStrategy.Normal,
            LayerRepairTrigger.LayerSetter,
            countBottomMostFailure: false,
            forceApply: true);

        engine.ResetObservationEpisode();
        api.SetErrorCode = 0;
        api.SetCalls.Clear();
        LayerRepairResult result = engine.Repair(
            new IntPtr(10),
            LayerStrategy.Normal,
            LayerRepairTrigger.SessionUnlock,
            countBottomMostFailure: false);

        Assert.Equal(LayerRepairOutcome.Healthy, result.Outcome);
        Assert.Equal(WindowInterop.HWND_NOTOPMOST, Assert.Single(api.SetCalls).InsertAfter);
    }

    [Fact]
    public void style_read_failure_does_not_guess_or_call_set_window_pos()
    {
        var api = new FakeWindowLayerApi { StyleErrorCode = 6 };
        var engine = new WindowLayerRepairEngine(api);

        LayerRepairResult result = engine.Repair(
            new IntPtr(10),
            LayerStrategy.TopMost,
            LayerRepairTrigger.Timer,
            countBottomMostFailure: true);

        Assert.Equal(LayerRepairOutcome.Failed, result.Outcome);
        Assert.Equal(LayerFailureKind.StyleReadFailed, result.ApplyResult.FailureKind);
        Assert.Empty(api.SetCalls);
    }

    [Fact]
    public void set_window_pos_failure_preserves_the_immediate_error()
    {
        var api = new FakeWindowLayerApi { SetErrorCode = 87 };
        var engine = new WindowLayerRepairEngine(api);

        LayerRepairResult result = engine.Repair(
            new IntPtr(10),
            LayerStrategy.BottomMost,
            LayerRepairTrigger.Timer,
            countBottomMostFailure: true);

        Assert.Equal(LayerRepairOutcome.Failed, result.Outcome);
        Assert.Equal(87, result.ApplyResult.ErrorCode);
        Assert.Equal(0, api.GetStyleCalls);
    }

    [Theory]
    [InlineData(5)]
    [InlineData(998)]
    public void documented_generic_errors_are_counted_as_bottommost_failures(int errorCode)
    {
        var api = new FakeWindowLayerApi { SetErrorCode = errorCode };
        var engine = new WindowLayerRepairEngine(api);

        LayerRepairResult first = engine.Repair(
            new IntPtr(10),
            LayerStrategy.BottomMost,
            LayerRepairTrigger.Timer,
            countBottomMostFailure: true);
        LayerRepairResult second = engine.Repair(
            new IntPtr(10),
            LayerStrategy.BottomMost,
            LayerRepairTrigger.Timer,
            countBottomMostFailure: true);
        LayerRepairResult third = engine.Repair(
            new IntPtr(10),
            LayerStrategy.BottomMost,
            LayerRepairTrigger.Timer,
            countBottomMostFailure: true);

        Assert.Equal(1, first.ConsecutiveFailures);
        Assert.Equal(2, second.ConsecutiveFailures);
        Assert.Equal(3, third.ConsecutiveFailures);
        Assert.Equal(LayerRepairOutcome.FallbackPending, third.Outcome);
    }

    [Fact]
    public void invalid_window_handle_resets_without_fallback()
    {
        var api = new FakeWindowLayerApi { SetErrorCode = 5 };
        var engine = new WindowLayerRepairEngine(api);
        _ = engine.Repair(new IntPtr(10), LayerStrategy.BottomMost, LayerRepairTrigger.Timer, true);
        _ = engine.Repair(new IntPtr(10), LayerStrategy.BottomMost, LayerRepairTrigger.Timer, true);
        api.SetErrorCode = WindowInterop.ERROR_INVALID_WINDOW_HANDLE;

        LayerRepairResult result = engine.Repair(
            new IntPtr(10),
            LayerStrategy.BottomMost,
            LayerRepairTrigger.Timer,
            countBottomMostFailure: true);

        Assert.Equal(LayerRepairOutcome.Skipped, result.Outcome);
        Assert.Equal(0, engine.BottomMostConsecutiveFailures);
        Assert.Equal(LayerHealthState.Unknown, engine.Health);
    }

    [Fact]
    public void managed_exception_is_nonthrowing_not_counted_and_retryable()
    {
        var api = new FakeWindowLayerApi { ThrowOnSet = true };
        var engine = new WindowLayerRepairEngine(api);

        LayerRepairResult failed = engine.Repair(
            new IntPtr(10),
            LayerStrategy.BottomMost,
            LayerRepairTrigger.Timer,
            countBottomMostFailure: true);
        api.ThrowOnSet = false;
        LayerRepairResult recovered = engine.Repair(
            new IntPtr(10),
            LayerStrategy.BottomMost,
            LayerRepairTrigger.Timer,
            countBottomMostFailure: true);

        Assert.Equal(LayerFailureKind.ManagedException, failed.ApplyResult.FailureKind);
        Assert.Equal(0, failed.ConsecutiveFailures);
        Assert.Equal(LayerRepairOutcome.Healthy, recovered.Outcome);
    }

    [Fact]
    public void ancillary_managed_exception_does_not_degrade_layer_health()
    {
        var api = new FakeWindowLayerApi { IsTopMost = true };
        var diagnostics = new List<LayerDiagnosticEvent>();
        var engine = new WindowLayerRepairEngine(api, diagnostics.Add);
        _ = engine.Repair(new IntPtr(10), LayerStrategy.TopMost, LayerRepairTrigger.Timer, true);

        engine.RecordNonRepairManagedException(
            LayerStrategy.TopMost,
            LayerRepairTrigger.WindowPositionChanging,
            LayerFailureKind.HookObservationException);
        _ = engine.Repair(new IntPtr(10), LayerStrategy.TopMost, LayerRepairTrigger.Timer, true);

        Assert.Equal(LayerHealthState.Healthy, engine.Health);
        Assert.Contains(diagnostics, item => item.FailureKind == LayerFailureKind.HookObservationException);
        Assert.DoesNotContain(diagnostics, item => item.Category == LayerDiagnosticCategory.RepairRecovered);
    }

    [Fact]
    public void failure_fingerprint_is_deduplicated_until_episode_reset()
    {
        var api = new FakeWindowLayerApi { SetErrorCode = 5 };
        var diagnostics = new List<LayerDiagnosticEvent>();
        var engine = new WindowLayerRepairEngine(api, diagnostics.Add);

        _ = engine.Repair(new IntPtr(10), LayerStrategy.BottomMost, LayerRepairTrigger.Timer, true);
        _ = engine.Repair(new IntPtr(10), LayerStrategy.BottomMost, LayerRepairTrigger.Timer, true);
        Assert.Single(diagnostics, item => item.Category == LayerDiagnosticCategory.ApplyFailure);

        engine.ResetEpisode();
        _ = engine.Repair(new IntPtr(10), LayerStrategy.BottomMost, LayerRepairTrigger.Timer, true);
        Assert.Equal(2, diagnostics.Count(item => item.Category == LayerDiagnosticCategory.ApplyFailure));
    }

    [Fact]
    public void no_zorder_window_pos_is_never_modified()
    {
        var api = new FakeWindowLayerApi { IsTopMost = false };
        WindowInterop.WINDOWPOS original = new()
        {
            hwnd = new IntPtr(10),
            hwndInsertAfter = WindowInterop.HWND_NOTOPMOST,
            flags = WindowInterop.SWP_NOZORDER | WindowInterop.SWP_NOACTIVATE,
        };

        (bool changed, WindowInterop.WINDOWPOS actual) = Rewrite(original, LayerStrategy.TopMost, false, api);

        Assert.False(changed);
        Assert.Equal(original, actual);
        Assert.Equal(0, api.GetStyleCalls);
    }

    [Fact]
    public void topmost_rewrite_only_blocks_explicit_non_topmost_demotion()
    {
        var api = new FakeWindowLayerApi { IsTopMost = false };

        AssertRewrite(WindowInterop.HWND_NOTOPMOST, expectedChanged: true, api);
        AssertRewrite(WindowInterop.HWND_BOTTOM, expectedChanged: true, api);
        AssertRewrite(WindowInterop.HWND_TOPMOST, expectedChanged: false, api);
        AssertRewrite(WindowInterop.HWND_TOP, expectedChanged: false, api);
        AssertRewrite(new IntPtr(44), expectedChanged: true, api);
        api.IsTopMost = true;
        AssertRewrite(new IntPtr(45), expectedChanged: false, api);

        Assert.Equal(2, api.GetStyleCalls);
    }

    [Fact]
    public void topmost_rewrite_policy_is_pure_for_special_and_observed_targets()
    {
        Assert.True(BottomMostStrategy.ShouldRewriteTopMost(WindowInterop.HWND_NOTOPMOST, null));
        Assert.True(BottomMostStrategy.ShouldRewriteTopMost(WindowInterop.HWND_BOTTOM, null));
        Assert.False(BottomMostStrategy.ShouldRewriteTopMost(WindowInterop.HWND_TOPMOST, null));
        Assert.False(BottomMostStrategy.ShouldRewriteTopMost(WindowInterop.HWND_TOP, null));
        Assert.True(BottomMostStrategy.ShouldRewriteTopMost(
            new IntPtr(44),
            new WindowStyleObservation(true, false, 0)));
        Assert.False(BottomMostStrategy.ShouldRewriteTopMost(
            new IntPtr(44),
            new WindowStyleObservation(true, true, 0)));
        Assert.False(BottomMostStrategy.ShouldRewriteTopMost(
            new IntPtr(44),
            new WindowStyleObservation(false, false, 6)));
    }

    [Fact]
    public void suppressed_bottommost_rewrite_leaves_fallback_request_unchanged()
    {
        var api = new FakeWindowLayerApi();
        WindowInterop.WINDOWPOS original = new()
        {
            hwnd = new IntPtr(10),
            hwndInsertAfter = WindowInterop.HWND_NOTOPMOST,
            flags = WindowInterop.SWP_NOACTIVATE,
        };

        (bool changed, WindowInterop.WINDOWPOS actual) = Rewrite(original, LayerStrategy.BottomMost, true, api);

        Assert.False(changed);
        Assert.Equal(original, actual);
    }

    private static void AssertRewrite(
        IntPtr insertAfter,
        bool expectedChanged,
        FakeWindowLayerApi api)
    {
        WindowInterop.WINDOWPOS original = new()
        {
            hwnd = new IntPtr(10),
            hwndInsertAfter = insertAfter,
            flags = WindowInterop.SWP_NOACTIVATE,
        };
        (bool changed, WindowInterop.WINDOWPOS actual) = Rewrite(original, LayerStrategy.TopMost, false, api);

        Assert.Equal(expectedChanged, changed);
        Assert.Equal(
            expectedChanged ? WindowInterop.HWND_TOPMOST : insertAfter,
            actual.hwndInsertAfter);
        Assert.Equal(original.flags, actual.flags);
    }

    private static (bool Changed, WindowInterop.WINDOWPOS Position) Rewrite(
        WindowInterop.WINDOWPOS position,
        LayerStrategy strategy,
        bool suppress,
        IWindowLayerApi api)
    {
        IntPtr pointer = Marshal.AllocHGlobal(Marshal.SizeOf<WindowInterop.WINDOWPOS>());
        try
        {
            Marshal.StructureToPtr(position, pointer, fDeleteOld: false);
            bool changed = BottomMostStrategy.RewriteWindowPosForLayer(pointer, strategy, suppress, api);
            return (changed, Marshal.PtrToStructure<WindowInterop.WINDOWPOS>(pointer));
        }
        finally
        {
            Marshal.FreeHGlobal(pointer);
        }
    }

    private sealed class FakeWindowLayerApi : IWindowLayerApi
    {
        public bool IsTopMost { get; set; }
        public int StyleErrorCode { get; set; }
        public int SetErrorCode { get; set; }
        public bool ThrowOnSet { get; set; }
        public int GetStyleCalls { get; private set; }
        public List<WindowSetCall> SetCalls { get; } = [];

        public WindowStyleObservation GetExtendedStyle(IntPtr hWnd)
        {
            GetStyleCalls++;
            return StyleErrorCode == 0
                ? new WindowStyleObservation(true, IsTopMost, 0)
                : new WindowStyleObservation(false, false, StyleErrorCode);
        }

        public WindowPositionCallResult SetWindowPosition(IntPtr hWnd, IntPtr hWndInsertAfter, uint flags)
        {
            if (ThrowOnSet)
            {
                throw new InvalidOperationException("injected");
            }
            SetCalls.Add(new WindowSetCall(hWnd, hWndInsertAfter, flags));
            if (SetErrorCode != 0)
            {
                return new WindowPositionCallResult(false, SetErrorCode);
            }
            IsTopMost = hWndInsertAfter == WindowInterop.HWND_TOPMOST;
            return new WindowPositionCallResult(true, 0);
        }
    }

    private readonly record struct WindowSetCall(IntPtr HWnd, IntPtr InsertAfter, uint Flags);
}
