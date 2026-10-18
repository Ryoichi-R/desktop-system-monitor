using DesktopSystemMonitor.Windows.Window;

namespace DesktopSystemMonitor.App.Startup;

[Flags]
internal enum LayerRepairSuspensionReason
{
    None = 0,
    SessionLocked = 1,
    SettingsDialog = 2,
}

/// <summary>
/// Aggregates independent reasons to suspend MainWindow/BackgroundLayer
/// BottomMost repair so one caller (e.g. session unlock) cannot resume
/// repair while another reason (e.g. the settings dialog) still holds it
/// suspended. Must only be used from the UI dispatcher thread, matching
/// every other layer-repair entry point in the app.
/// </summary>
internal sealed class LayerRepairSuspensionCoordinator
{
    private readonly Action<bool> _setMainWindowSuspended;
    private readonly Action<LayerRepairTrigger> _reapplyMainWindow;
    private readonly Action<bool, LayerRepairTrigger> _setBackgroundSuspended;
    private readonly Action<string, Exception?> _recordDiagnostic;
    private LayerRepairSuspensionReason _reasons;

    internal LayerRepairSuspensionCoordinator(
        Action<bool> setMainWindowSuspended,
        Action<LayerRepairTrigger> reapplyMainWindow,
        Action<bool, LayerRepairTrigger> setBackgroundSuspended,
        Action<string, Exception?> recordDiagnostic)
    {
        _setMainWindowSuspended = setMainWindowSuspended ?? throw new ArgumentNullException(nameof(setMainWindowSuspended));
        _reapplyMainWindow = reapplyMainWindow ?? throw new ArgumentNullException(nameof(reapplyMainWindow));
        _setBackgroundSuspended = setBackgroundSuspended ?? throw new ArgumentNullException(nameof(setBackgroundSuspended));
        _recordDiagnostic = recordDiagnostic ?? throw new ArgumentNullException(nameof(recordDiagnostic));
    }

    internal LayerRepairSuspensionReason CurrentReasons => _reasons;

    /// <summary>
    /// Acquires <paramref name="reason"/> and returns a lease that releases it
    /// exactly once, on first Dispose. If suspension fails, the reason is not
    /// recorded and the returned lease is a harmless no-op on Dispose.
    /// </summary>
    internal IDisposable Acquire(LayerRepairSuspensionReason reason, LayerRepairTrigger resumeTrigger)
    {
        SetReason(reason, active: true, resumeTrigger);
        return new Lease(this, reason, resumeTrigger);
    }

    /// <summary>
    /// Adds or removes <paramref name="reason"/> from the suspended set.
    /// MainWindow/BackgroundLayer are only suspended on the None-to-non-None
    /// transition and only resumed on the non-None-to-None transition, so
    /// overlapping reasons never let each other's release re-enable repair.
    /// </summary>
    internal void SetReason(LayerRepairSuspensionReason reason, bool active, LayerRepairTrigger resumeTrigger)
    {
        LayerRepairSuspensionReason before = _reasons;
        LayerRepairSuspensionReason after = active ? (before | reason) : (before & ~reason);
        if (after == before)
        {
            return;
        }

        if (before == LayerRepairSuspensionReason.None && !TrySuspend())
        {
            // Suspension failed: don't record the reason, so a later matching
            // release is a harmless no-op instead of a false resume.
            return;
        }

        _reasons = after;

        if (after == LayerRepairSuspensionReason.None)
        {
            Resume(resumeTrigger);
        }
    }

    private bool TrySuspend()
    {
        if (!TryInvoke(() => _setMainWindowSuspended(true), "layer-repair-suspend-main-failure"))
        {
            return false;
        }

        if (TryInvoke(
            () => _setBackgroundSuspended(true, LayerRepairTrigger.ExplicitReapply),
            "layer-repair-suspend-background-failure"))
        {
            return true;
        }

        // Roll back MainWindow so Main/Background suspension state stays
        // consistent instead of leaving MainWindow suspended with no reason held.
        _ = TryInvoke(() => _setMainWindowSuspended(false), "layer-repair-suspend-rollback-failure");
        return false;
    }

    private void Resume(LayerRepairTrigger trigger)
    {
        bool mainResumed = TryInvoke(() => _setMainWindowSuspended(false), "layer-repair-resume-main-failure");
        _ = TryInvoke(() => _setBackgroundSuspended(false, trigger), "layer-repair-resume-background-failure");
        if (mainResumed)
        {
            // MainWindow.ReapplyWindowStyles no-ops while its own suspended
            // flag is still true, so only reapply once the resume actually landed.
            _ = TryInvoke(() => _reapplyMainWindow(trigger), "layer-repair-resume-reapply-failure");
        }
    }

    private bool TryInvoke(Action action, string failureCategory)
    {
        try
        {
            action();
            return true;
        }
        catch (Exception ex)
        {
            TryRecord(failureCategory, ex);
            return false;
        }
    }

    private void TryRecord(string category, Exception? exception)
    {
        try
        {
            _recordDiagnostic(category, exception);
        }
        catch
        {
            // Diagnostics are best effort and must not destabilize suspension state.
        }
    }

    private sealed class Lease : IDisposable
    {
        private readonly LayerRepairSuspensionCoordinator _owner;
        private readonly LayerRepairSuspensionReason _reason;
        private readonly LayerRepairTrigger _resumeTrigger;
        private int _disposed;

        internal Lease(
            LayerRepairSuspensionCoordinator owner,
            LayerRepairSuspensionReason reason,
            LayerRepairTrigger resumeTrigger)
        {
            _owner = owner;
            _reason = reason;
            _resumeTrigger = resumeTrigger;
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
            {
                return;
            }
            _owner.SetReason(_reason, active: false, _resumeTrigger);
        }
    }
}
