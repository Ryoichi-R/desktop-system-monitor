using System;
using System.Collections.Generic;
using DesktopSystemMonitor.App.Startup;
using DesktopSystemMonitor.Windows.Window;
using Xunit;

namespace DesktopSystemMonitor.App.Tests;

public sealed class LayerRepairSuspensionCoordinatorTests
{
    [Fact]
    public void first_reason_suspends_main_and_background_once()
    {
        var calls = new List<string>();
        var coordinator = new LayerRepairSuspensionCoordinator(
            suspended => calls.Add($"main-suspend:{suspended}"),
            trigger => calls.Add($"reapply:{trigger}"),
            (suspended, trigger) => calls.Add($"background-suspend:{suspended}:{trigger}"),
            (_, _) => { });

        using IDisposable lease = coordinator.Acquire(
            LayerRepairSuspensionReason.SettingsDialog,
            LayerRepairTrigger.SettingsFlowCompleted);

        Assert.Equal(["main-suspend:True", "background-suspend:True:ExplicitReapply"], calls);
        Assert.Equal(LayerRepairSuspensionReason.SettingsDialog, coordinator.CurrentReasons);
    }

    [Fact]
    public void duplicate_acquire_is_idempotent()
    {
        var calls = new List<string>();
        var coordinator = new LayerRepairSuspensionCoordinator(
            suspended => calls.Add($"main-suspend:{suspended}"),
            trigger => calls.Add($"reapply:{trigger}"),
            (suspended, trigger) => calls.Add($"background-suspend:{suspended}:{trigger}"),
            (_, _) => { });

        using IDisposable first = coordinator.Acquire(
            LayerRepairSuspensionReason.SettingsDialog,
            LayerRepairTrigger.SettingsFlowCompleted);
        using IDisposable second = coordinator.Acquire(
            LayerRepairSuspensionReason.SettingsDialog,
            LayerRepairTrigger.SettingsFlowCompleted);

        Assert.Equal(["main-suspend:True", "background-suspend:True:ExplicitReapply"], calls);
        Assert.Equal(LayerRepairSuspensionReason.SettingsDialog, coordinator.CurrentReasons);
    }

    [Fact]
    public void releasing_one_reason_keeps_other_reason_suspended()
    {
        var calls = new List<string>();
        var coordinator = new LayerRepairSuspensionCoordinator(
            suspended => calls.Add($"main-suspend:{suspended}"),
            trigger => calls.Add($"reapply:{trigger}"),
            (suspended, trigger) => calls.Add($"background-suspend:{suspended}:{trigger}"),
            (_, _) => { });
        coordinator.SetReason(LayerRepairSuspensionReason.SettingsDialog, true, LayerRepairTrigger.SettingsFlowCompleted);
        coordinator.SetReason(LayerRepairSuspensionReason.SessionLocked, true, LayerRepairTrigger.SessionUnlock);
        calls.Clear();

        coordinator.SetReason(LayerRepairSuspensionReason.SessionLocked, false, LayerRepairTrigger.SessionUnlock);

        Assert.Empty(calls);
        Assert.Equal(LayerRepairSuspensionReason.SettingsDialog, coordinator.CurrentReasons);

        coordinator.SetReason(LayerRepairSuspensionReason.SettingsDialog, false, LayerRepairTrigger.SettingsFlowCompleted);

        Assert.Equal(
            ["main-suspend:False", "background-suspend:False:SettingsFlowCompleted", "reapply:SettingsFlowCompleted"],
            calls);
        Assert.Equal(LayerRepairSuspensionReason.None, coordinator.CurrentReasons);
    }

    [Fact]
    public void session_lock_acquired_first_keeps_suspended_until_settings_dialog_releases()
    {
        var calls = new List<string>();
        var coordinator = new LayerRepairSuspensionCoordinator(
            suspended => calls.Add($"main-suspend:{suspended}"),
            trigger => calls.Add($"reapply:{trigger}"),
            (suspended, trigger) => calls.Add($"background-suspend:{suspended}:{trigger}"),
            (_, _) => { });

        // Sequence: SessionLock -> SettingsDialog acquire -> SettingsDialog
        // release -> SessionUnlock. Releasing SettingsDialog first must not
        // resume repair while SessionLocked is still held; only the final
        // SessionUnlock (last reason cleared) resumes.
        coordinator.SetReason(LayerRepairSuspensionReason.SessionLocked, true, LayerRepairTrigger.SessionUnlock);
        coordinator.SetReason(LayerRepairSuspensionReason.SettingsDialog, true, LayerRepairTrigger.SettingsFlowCompleted);
        calls.Clear();

        coordinator.SetReason(LayerRepairSuspensionReason.SettingsDialog, false, LayerRepairTrigger.SettingsFlowCompleted);

        Assert.Empty(calls);
        Assert.Equal(LayerRepairSuspensionReason.SessionLocked, coordinator.CurrentReasons);

        coordinator.SetReason(LayerRepairSuspensionReason.SessionLocked, false, LayerRepairTrigger.SessionUnlock);

        Assert.Equal(
            ["main-suspend:False", "background-suspend:False:SessionUnlock", "reapply:SessionUnlock"],
            calls);
        Assert.Equal(LayerRepairSuspensionReason.None, coordinator.CurrentReasons);
    }

    [Fact]
    public void last_reason_release_resumes_main_then_background_then_reapplies()
    {
        var calls = new List<string>();
        var coordinator = new LayerRepairSuspensionCoordinator(
            suspended => calls.Add($"main-suspend:{suspended}"),
            trigger => calls.Add($"reapply:{trigger}"),
            (suspended, trigger) => calls.Add($"background-suspend:{suspended}:{trigger}"),
            (_, _) => { });
        IDisposable lease = coordinator.Acquire(
            LayerRepairSuspensionReason.SettingsDialog,
            LayerRepairTrigger.SettingsFlowCompleted);
        calls.Clear();

        lease.Dispose();

        Assert.Equal(
            ["main-suspend:False", "background-suspend:False:SettingsFlowCompleted", "reapply:SettingsFlowCompleted"],
            calls);
        Assert.Equal(LayerRepairSuspensionReason.None, coordinator.CurrentReasons);
    }

    [Fact]
    public void lease_disposes_once()
    {
        int resumeCalls = 0;
        var coordinator = new LayerRepairSuspensionCoordinator(
            _ => { },
            _ => { },
            (suspended, _) =>
            {
                if (!suspended)
                {
                    resumeCalls++;
                }
            },
            (_, _) => { });
        IDisposable lease = coordinator.Acquire(
            LayerRepairSuspensionReason.SettingsDialog,
            LayerRepairTrigger.SettingsFlowCompleted);

        lease.Dispose();
        lease.Dispose();

        Assert.Equal(1, resumeCalls);
    }

    [Fact]
    public void releasing_reason_not_currently_held_is_a_no_op()
    {
        var calls = new List<string>();
        var coordinator = new LayerRepairSuspensionCoordinator(
            suspended => calls.Add($"main-suspend:{suspended}"),
            trigger => calls.Add($"reapply:{trigger}"),
            (suspended, trigger) => calls.Add($"background-suspend:{suspended}:{trigger}"),
            (_, _) => { });

        coordinator.SetReason(LayerRepairSuspensionReason.SettingsDialog, false, LayerRepairTrigger.SettingsFlowCompleted);

        Assert.Empty(calls);
        Assert.Equal(LayerRepairSuspensionReason.None, coordinator.CurrentReasons);
    }

    [Fact]
    public void background_suspend_failure_rolls_back_main_and_does_not_record_reason()
    {
        var calls = new List<string>();
        var diagnostics = new List<string>();
        var coordinator = new LayerRepairSuspensionCoordinator(
            suspended => calls.Add($"main-suspend:{suspended}"),
            trigger => calls.Add($"reapply:{trigger}"),
            (suspended, trigger) =>
            {
                calls.Add($"background-suspend:{suspended}:{trigger}");
                if (suspended)
                {
                    throw new InvalidOperationException("background injected");
                }
            },
            (category, _) => diagnostics.Add(category));

        IDisposable lease = coordinator.Acquire(
            LayerRepairSuspensionReason.SettingsDialog,
            LayerRepairTrigger.SettingsFlowCompleted);

        Assert.Equal(
            ["main-suspend:True", "background-suspend:True:ExplicitReapply", "main-suspend:False"],
            calls);
        Assert.Equal(LayerRepairSuspensionReason.None, coordinator.CurrentReasons);
        Assert.Contains("layer-repair-suspend-background-failure", diagnostics);

        // A lease from a failed acquisition is a harmless no-op on Dispose.
        var escaped = Record.Exception(lease.Dispose);
        Assert.Null(escaped);
        Assert.Equal(3, calls.Count);
    }

    [Fact]
    public void main_suspend_failure_prevents_background_suspend_and_does_not_record_reason()
    {
        var calls = new List<string>();
        var diagnostics = new List<string>();
        var coordinator = new LayerRepairSuspensionCoordinator(
            suspended =>
            {
                calls.Add($"main-suspend:{suspended}");
                if (suspended)
                {
                    throw new InvalidOperationException("main injected");
                }
            },
            trigger => calls.Add($"reapply:{trigger}"),
            (suspended, trigger) => calls.Add($"background-suspend:{suspended}:{trigger}"),
            (category, _) => diagnostics.Add(category));

        coordinator.Acquire(LayerRepairSuspensionReason.SettingsDialog, LayerRepairTrigger.SettingsFlowCompleted);

        Assert.Equal(["main-suspend:True"], calls);
        Assert.Equal(LayerRepairSuspensionReason.None, coordinator.CurrentReasons);
        Assert.Contains("layer-repair-suspend-main-failure", diagnostics);
    }

    [Fact]
    public void resume_failure_is_reported_without_skipping_remaining_cleanup()
    {
        var calls = new List<string>();
        var diagnostics = new List<string>();
        var coordinator = new LayerRepairSuspensionCoordinator(
            suspended =>
            {
                calls.Add($"main-suspend:{suspended}");
                if (!suspended)
                {
                    throw new InvalidOperationException("main resume injected");
                }
            },
            trigger => calls.Add($"reapply:{trigger}"),
            (suspended, trigger) => calls.Add($"background-suspend:{suspended}:{trigger}"),
            (category, _) => diagnostics.Add(category));
        IDisposable lease = coordinator.Acquire(
            LayerRepairSuspensionReason.SettingsDialog,
            LayerRepairTrigger.SettingsFlowCompleted);
        calls.Clear();

        var escaped = Record.Exception(lease.Dispose);

        Assert.Null(escaped);
        Assert.Equal(
            ["main-suspend:False", "background-suspend:False:SettingsFlowCompleted"],
            calls);
        Assert.Contains("layer-repair-resume-main-failure", diagnostics);
        Assert.Equal(LayerRepairSuspensionReason.None, coordinator.CurrentReasons);
    }

    [Fact]
    public void diagnostic_sink_failure_does_not_escape_when_suspend_fails()
    {
        var coordinator = new LayerRepairSuspensionCoordinator(
            suspended => throw new InvalidOperationException("main injected"),
            _ => { },
            (_, _) => { },
            (_, _) => throw new InvalidOperationException("diagnostic injected"));

        var escaped = Record.Exception(() =>
            coordinator.Acquire(LayerRepairSuspensionReason.SettingsDialog, LayerRepairTrigger.SettingsFlowCompleted));

        Assert.Null(escaped);
        Assert.Equal(LayerRepairSuspensionReason.None, coordinator.CurrentReasons);
    }
}
