namespace DesktopSystemMonitor.Windows.Window;

internal enum LayerRepairTrigger
{
    LayerSetter,
    SourceInitialized,
    ExplicitReapply,
    Timer,
    TaskbarCreated,
    WindowActivated,
    DisplayChanged,
    SessionUnlock,
    SettingsFlowCompleted,
    TraySelection,
    WindowPositionChanging,
    ClickThroughSetter,
    VisibilityChanged,
}

internal enum LayerFailureKind
{
    None,
    SetWindowPosFailed,
    StyleReadFailed,
    StyleMismatch,
    ManagedException,
    StyleMutationException,
    HookObservationException,
}

internal enum LayerEffectiveState
{
    Unknown,
    TopMost,
    NonTopMost,
}

internal enum LayerDiagnosticCategory
{
    ApplyFailure,
    RepairEnter,
    RepairRecovered,
    ManagedException,
    FallbackSubscriberFailure,
}

internal enum LayerRepairOutcome
{
    Skipped,
    Healthy,
    Failed,
    FallbackPending,
}

internal enum LayerHealthState
{
    Unknown,
    Healthy,
    Degraded,
}

internal readonly record struct LayerDiagnosticEvent(
    LayerDiagnosticCategory Category,
    LayerStrategy DesiredStrategy,
    LayerEffectiveState EffectiveState,
    LayerRepairTrigger Trigger,
    LayerFailureKind FailureKind,
    int? ErrorCode,
    int ConsecutiveFailures);

internal readonly record struct LayerApplyResult(
    LayerStrategy DesiredStrategy,
    bool SetWindowPosSucceeded,
    bool? ObservedTopMost,
    int ErrorCode,
    LayerFailureKind FailureKind)
{
    public bool Succeeded => FailureKind == LayerFailureKind.None;

    public LayerEffectiveState EffectiveState => ObservedTopMost switch
    {
        true => LayerEffectiveState.TopMost,
        false => LayerEffectiveState.NonTopMost,
        null => LayerEffectiveState.Unknown,
    };
}

internal readonly record struct LayerRepairResult(
    LayerRepairOutcome Outcome,
    LayerApplyResult ApplyResult,
    int ConsecutiveFailures);

internal static class WindowLayerOperation
{
    private const uint LayerFlags = WindowInterop.SWP_NOMOVE
        | WindowInterop.SWP_NOSIZE
        | WindowInterop.SWP_NOACTIVATE;

    public static LayerApplyResult Apply(
        IWindowLayerApi api,
        IntPtr hWnd,
        LayerStrategy desiredStrategy)
    {
        IntPtr insertAfter = desiredStrategy switch
        {
            LayerStrategy.BottomMost => WindowInterop.HWND_BOTTOM,
            LayerStrategy.TopMost => WindowInterop.HWND_TOPMOST,
            _ => WindowInterop.HWND_NOTOPMOST,
        };
        WindowPositionCallResult position = api.SetWindowPosition(hWnd, insertAfter, LayerFlags);
        if (!position.Succeeded)
        {
            return new LayerApplyResult(
                desiredStrategy,
                false,
                null,
                position.ErrorCode,
                LayerFailureKind.SetWindowPosFailed);
        }

        WindowStyleObservation style = api.GetExtendedStyle(hWnd);
        if (!style.Succeeded)
        {
            return new LayerApplyResult(
                desiredStrategy,
                true,
                null,
                style.ErrorCode,
                LayerFailureKind.StyleReadFailed);
        }

        bool expectedTopMost = desiredStrategy == LayerStrategy.TopMost;
        if (style.IsTopMost != expectedTopMost)
        {
            return new LayerApplyResult(
                desiredStrategy,
                true,
                style.IsTopMost,
                0,
                LayerFailureKind.StyleMismatch);
        }

        return new LayerApplyResult(desiredStrategy, true, style.IsTopMost, 0, LayerFailureKind.None);
    }
}

internal sealed class WindowLayerRepairEngine
{
    private const int BottomMostFallbackThreshold = 3;

    private readonly IWindowLayerApi _api;
    private readonly Action<LayerDiagnosticEvent>? _diagnostic;
    private readonly HashSet<LayerFailureFingerprint> _reportedFailures = [];
    private IntPtr _episodeHwnd;
    private LayerStrategy _episodeStrategy;
    private bool _hasEpisode;
    private LayerHealthState _health;
    private int _bottomMostConsecutiveFailures;
    private bool _forceApplyPending;

    public WindowLayerRepairEngine(
        IWindowLayerApi api,
        Action<LayerDiagnosticEvent>? diagnostic = null)
    {
        _api = api ?? throw new ArgumentNullException(nameof(api));
        _diagnostic = diagnostic;
    }

    internal LayerHealthState Health => _health;

    internal int BottomMostConsecutiveFailures => _bottomMostConsecutiveFailures;

    public LayerRepairResult Repair(
        IntPtr hWnd,
        LayerStrategy desiredStrategy,
        LayerRepairTrigger trigger,
        bool countBottomMostFailure,
        bool forceApply = false)
    {
        if (hWnd == IntPtr.Zero)
        {
            ResetEpisode();
            return Skipped(desiredStrategy);
        }

        EnsureEpisode(hWnd, desiredStrategy);
        try
        {
            bool mustForceApply = forceApply || _forceApplyPending;
            if (!mustForceApply && desiredStrategy is (LayerStrategy.TopMost or LayerStrategy.Normal))
            {
                WindowStyleObservation before = _api.GetExtendedStyle(hWnd);
                if (!before.Succeeded)
                {
                    if (before.ErrorCode == WindowInterop.ERROR_INVALID_WINDOW_HANDLE)
                    {
                        ResetEpisode();
                        return Skipped(desiredStrategy);
                    }

                    LayerApplyResult readFailure = new(
                        desiredStrategy,
                        false,
                        null,
                        before.ErrorCode,
                        LayerFailureKind.StyleReadFailed);
                    return RegisterFailure(readFailure, trigger, countBottomMostFailure);
                }

                bool expectedTopMost = desiredStrategy == LayerStrategy.TopMost;
                if (before.IsTopMost == expectedTopMost)
                {
                    LayerApplyResult healthy = new(
                        desiredStrategy,
                        false,
                        before.IsTopMost,
                        0,
                        LayerFailureKind.None);
                    return RegisterSuccess(healthy, trigger);
                }

                EnterRepair(desiredStrategy, before.IsTopMost, trigger);
            }

            if (mustForceApply && desiredStrategy is (LayerStrategy.TopMost or LayerStrategy.Normal))
            {
                _forceApplyPending = true;
            }
            LayerApplyResult applied = WindowLayerOperation.Apply(_api, hWnd, desiredStrategy);
            if (applied.Succeeded)
            {
                return RegisterSuccess(applied, trigger);
            }
            if (applied.ErrorCode == WindowInterop.ERROR_INVALID_WINDOW_HANDLE)
            {
                ResetEpisode();
                return Skipped(desiredStrategy);
            }
            return RegisterFailure(applied, trigger, countBottomMostFailure);
        }
        catch
        {
            return RecordManagedException(desiredStrategy, trigger);
        }
    }

    public LayerRepairResult RecordFallbackFailure(
        LayerApplyResult result,
        LayerRepairTrigger trigger)
    {
        if (result.ErrorCode == WindowInterop.ERROR_INVALID_WINDOW_HANDLE)
        {
            ResetEpisode();
            return Skipped(result.DesiredStrategy);
        }
        return RegisterFailure(result, trigger, countBottomMostFailure: false);
    }

    public LayerRepairResult RecordManagedException(
        LayerStrategy desiredStrategy,
        LayerRepairTrigger trigger)
    {
        var result = new LayerApplyResult(
            desiredStrategy,
            false,
            null,
            0,
            LayerFailureKind.ManagedException);
        _health = LayerHealthState.Degraded;
        LayerFailureFingerprint fingerprint = new(desiredStrategy, result.FailureKind, 0);
        if (_reportedFailures.Add(fingerprint))
        {
            Emit(new LayerDiagnosticEvent(
                LayerDiagnosticCategory.ManagedException,
                desiredStrategy,
                LayerEffectiveState.Unknown,
                trigger,
                result.FailureKind,
                null,
                _bottomMostConsecutiveFailures));
        }
        return new LayerRepairResult(LayerRepairOutcome.Failed, result, _bottomMostConsecutiveFailures);
    }

    public void RecordNonRepairManagedException(
        LayerStrategy desiredStrategy,
        LayerRepairTrigger trigger,
        LayerFailureKind failureKind)
    {
        if (failureKind is not (LayerFailureKind.StyleMutationException
            or LayerFailureKind.HookObservationException))
        {
            throw new ArgumentOutOfRangeException(nameof(failureKind));
        }

        LayerFailureFingerprint fingerprint = new(desiredStrategy, failureKind, 0);
        if (_reportedFailures.Add(fingerprint))
        {
            Emit(new LayerDiagnosticEvent(
                LayerDiagnosticCategory.ManagedException,
                desiredStrategy,
                LayerEffectiveState.Unknown,
                trigger,
                failureKind,
                null,
                _bottomMostConsecutiveFailures));
        }
    }

    public void RecordFallbackSubscriberFailure(LayerRepairTrigger trigger)
    {
        Emit(new LayerDiagnosticEvent(
            LayerDiagnosticCategory.FallbackSubscriberFailure,
            LayerStrategy.Normal,
            LayerEffectiveState.NonTopMost,
            trigger,
            LayerFailureKind.ManagedException,
            null,
            0));
    }

    public void ResetEpisode()
    {
        _episodeHwnd = IntPtr.Zero;
        _episodeStrategy = default;
        _hasEpisode = false;
        _health = LayerHealthState.Unknown;
        _bottomMostConsecutiveFailures = 0;
        _forceApplyPending = false;
        _reportedFailures.Clear();
    }

    public void ResetObservationEpisode()
    {
        _health = LayerHealthState.Unknown;
        _bottomMostConsecutiveFailures = 0;
        _reportedFailures.Clear();
    }

    private void EnsureEpisode(IntPtr hWnd, LayerStrategy strategy)
    {
        if (_hasEpisode && _episodeHwnd == hWnd && _episodeStrategy == strategy)
        {
            return;
        }

        ResetEpisode();
        _episodeHwnd = hWnd;
        _episodeStrategy = strategy;
        _hasEpisode = true;
    }

    private void EnterRepair(
        LayerStrategy desiredStrategy,
        bool observedTopMost,
        LayerRepairTrigger trigger)
    {
        if (_health == LayerHealthState.Degraded)
        {
            return;
        }

        _health = LayerHealthState.Degraded;
        Emit(new LayerDiagnosticEvent(
            LayerDiagnosticCategory.RepairEnter,
            desiredStrategy,
            observedTopMost ? LayerEffectiveState.TopMost : LayerEffectiveState.NonTopMost,
            trigger,
            LayerFailureKind.StyleMismatch,
            null,
            _bottomMostConsecutiveFailures));
    }

    private LayerRepairResult RegisterSuccess(
        LayerApplyResult result,
        LayerRepairTrigger trigger)
    {
        bool recovered = _health == LayerHealthState.Degraded;
        _health = LayerHealthState.Healthy;
        _bottomMostConsecutiveFailures = 0;
        _forceApplyPending = false;
        if (recovered && result.DesiredStrategy is LayerStrategy.TopMost or LayerStrategy.Normal)
        {
            Emit(new LayerDiagnosticEvent(
                LayerDiagnosticCategory.RepairRecovered,
                result.DesiredStrategy,
                result.EffectiveState,
                trigger,
                LayerFailureKind.None,
                null,
                0));
        }
        return new LayerRepairResult(LayerRepairOutcome.Healthy, result, 0);
    }

    private LayerRepairResult RegisterFailure(
        LayerApplyResult result,
        LayerRepairTrigger trigger,
        bool countBottomMostFailure)
    {
        _health = LayerHealthState.Degraded;
        if (countBottomMostFailure && result.DesiredStrategy == LayerStrategy.BottomMost)
        {
            _bottomMostConsecutiveFailures = Math.Min(
                BottomMostFallbackThreshold,
                _bottomMostConsecutiveFailures + 1);
        }

        LayerFailureFingerprint fingerprint = new(
            result.DesiredStrategy,
            result.FailureKind,
            result.ErrorCode);
        if (_reportedFailures.Add(fingerprint))
        {
            Emit(new LayerDiagnosticEvent(
                LayerDiagnosticCategory.ApplyFailure,
                result.DesiredStrategy,
                result.EffectiveState,
                trigger,
                result.FailureKind,
                result.ErrorCode == 0 ? null : result.ErrorCode,
                _bottomMostConsecutiveFailures));
        }

        LayerRepairOutcome outcome = countBottomMostFailure
            && result.DesiredStrategy == LayerStrategy.BottomMost
            && _bottomMostConsecutiveFailures >= BottomMostFallbackThreshold
                ? LayerRepairOutcome.FallbackPending
                : LayerRepairOutcome.Failed;
        return new LayerRepairResult(outcome, result, _bottomMostConsecutiveFailures);
    }

    private void Emit(LayerDiagnosticEvent diagnosticEvent)
    {
        try
        {
            _diagnostic?.Invoke(diagnosticEvent);
        }
        catch
        {
            // Diagnostics are best effort and must not destabilize Z-order repair.
        }
    }

    private static LayerRepairResult Skipped(LayerStrategy desiredStrategy) => new(
        LayerRepairOutcome.Skipped,
        new LayerApplyResult(desiredStrategy, false, null, 0, LayerFailureKind.None),
        0);

    private readonly record struct LayerFailureFingerprint(
        LayerStrategy DesiredStrategy,
        LayerFailureKind FailureKind,
        int ErrorCode);
}
