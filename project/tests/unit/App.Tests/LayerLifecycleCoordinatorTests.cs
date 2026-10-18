using System;
using System.Collections.Generic;
using DesktopSystemMonitor.App.Startup;
using DesktopSystemMonitor.Core.Settings;
using DesktopSystemMonitor.Windows.Window;
using Microsoft.Win32;
using Xunit;

namespace DesktopSystemMonitor.App.Tests;

public sealed class LayerLifecycleCoordinatorTests
{
    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void fallback_partial_failure_keeps_runtime_normal_and_attempts_all_side_effects(
        bool failSave,
        bool failTray)
    {
        var calls = new List<string>();
        WindowLayerMode runtimeMode = WindowLayerMode.OnDesktop;
        bool settingsSaved = false;
        bool trayUpdated = false;
        var diagnostics = new List<(string Category, Exception Exception)>();

        var escaped = Record.Exception(() =>
            LayerLifecycleCoordinator.HandleLayerFallback(
                setRuntimeNormal: () =>
                {
                    calls.Add("runtime");
                    runtimeMode = WindowLayerMode.Normal;
                },
                saveSettings: () =>
                {
                    calls.Add("save");
                    if (failSave)
                    {
                        throw new InvalidOperationException("save injected");
                    }
                    settingsSaved = true;
                },
                updateTray: () =>
                {
                    calls.Add("tray");
                    if (failTray)
                    {
                        throw new InvalidOperationException("tray injected");
                    }
                    trayUpdated = true;
                },
                recordDiagnostic: (category, exception) =>
                    diagnostics.Add((category, exception))));

        Assert.Null(escaped);
        Assert.Equal(WindowLayerMode.Normal, runtimeMode);
        Assert.Equal(["runtime", "save", "tray"], calls);
        Assert.Equal(!failSave, settingsSaved);
        Assert.Equal(!failTray, trayUpdated);
        Assert.Equal("bottom-most-fallback", diagnostics[0].Category);
        string expectedFailureCategory = failSave
            ? "settings-save-failure"
            : "bottom-most-fallback-callback-failure";
        Assert.Contains(
            diagnostics,
            item => item.Category == expectedFailureCategory
                && item.Exception is InvalidOperationException);
    }

    [Fact]
    public void fallback_diagnostic_failure_does_not_block_runtime_save_or_tray()
    {
        var calls = new List<string>();

        var escaped = Record.Exception(() =>
            LayerLifecycleCoordinator.HandleLayerFallback(
                setRuntimeNormal: () => calls.Add("runtime"),
                saveSettings: () => calls.Add("save"),
                updateTray: () => calls.Add("tray"),
                recordDiagnostic: (_, _) => throw new InvalidOperationException("diagnostic injected")));

        Assert.Null(escaped);
        Assert.Equal(["runtime", "save", "tray"], calls);
    }

    [Fact]
    public void session_lock_and_unlock_apply_actions_in_the_defined_order()
    {
        var calls = new List<string>();

        LayerLifecycleCoordinator.HandleSessionSwitch(
            SessionSwitchReason.SessionLock,
            locked => calls.Add($"locked:{locked}"),
            () => calls.Add("sampling"),
            suspended => calls.Add($"suspended:{suspended}"),
            () => calls.Add("reapply"));
        LayerLifecycleCoordinator.HandleSessionSwitch(
            SessionSwitchReason.SessionUnlock,
            locked => calls.Add($"locked:{locked}"),
            () => calls.Add("sampling"),
            suspended => calls.Add($"suspended:{suspended}"),
            () => calls.Add("reapply"));

        Assert.Equal(
            [
                "locked:True", "sampling", "suspended:True",
                "locked:False", "sampling", "suspended:False", "reapply",
            ],
            calls);
    }

    [Fact]
    public void settings_completion_is_idempotent_and_reapplies_when_cleanup_throws()
    {
        int cleanupCalls = 0;
        int releaseCalls = 0;
        int reapplyCalls = 0;
        int errorCalls = 0;
        var scope = new SettingsFlowCompletionScope(
            cleanup: () =>
            {
                cleanupCalls++;
                throw new InvalidOperationException("injected");
            },
            releaseLayerRepair: () => releaseCalls++,
            reapplyWindowStyles: () => reapplyCalls++,
            onError: _ => errorCalls++);

        scope.Dispose();
        scope.Dispose();

        Assert.Equal(1, cleanupCalls);
        Assert.Equal(1, releaseCalls);
        Assert.Equal(1, reapplyCalls);
        Assert.Equal(1, errorCalls);
    }

    [Fact]
    public void settings_completion_runs_reapply_even_when_release_layer_repair_throws()
    {
        int cleanupCalls = 0;
        int releaseCalls = 0;
        int reapplyCalls = 0;
        int errorCalls = 0;
        var scope = new SettingsFlowCompletionScope(
            cleanup: () => cleanupCalls++,
            releaseLayerRepair: () =>
            {
                releaseCalls++;
                throw new InvalidOperationException("release injected");
            },
            reapplyWindowStyles: () => reapplyCalls++,
            onError: _ => errorCalls++);

        var escaped = Record.Exception(scope.Dispose);

        Assert.Null(escaped);
        Assert.Equal(1, cleanupCalls);
        Assert.Equal(1, releaseCalls);
        Assert.Equal(1, reapplyCalls);
        Assert.Equal(1, errorCalls);
    }

    [Fact]
    public void settings_completion_does_not_throw_when_reapply_and_error_reporting_throw()
    {
        int cleanupCalls = 0;
        int releaseCalls = 0;
        int reapplyCalls = 0;
        var scope = new SettingsFlowCompletionScope(
            cleanup: () => cleanupCalls++,
            releaseLayerRepair: () => releaseCalls++,
            reapplyWindowStyles: () =>
            {
                reapplyCalls++;
                throw new InvalidOperationException("reapply injected");
            },
            onError: _ => throw new InvalidOperationException("diagnostic injected"));

        var escaped = Record.Exception(scope.Dispose);

        Assert.Null(escaped);
        Assert.Equal(1, cleanupCalls);
        Assert.Equal(1, releaseCalls);
        Assert.Equal(1, reapplyCalls);
    }

    [Fact]
    public void settings_completion_runs_stages_in_cleanup_release_reapply_order()
    {
        var order = new List<string>();
        var scope = new SettingsFlowCompletionScope(
            cleanup: () => order.Add("cleanup"),
            releaseLayerRepair: () => order.Add("release"),
            reapplyWindowStyles: () => order.Add("reapply"),
            onError: _ => { });

        scope.Dispose();

        Assert.Equal(["cleanup", "release", "reapply"], order);
    }

    [Fact]
    public void unrelated_session_reason_only_updates_sampling_suspension()
    {
        var calls = new List<string>();

        LayerLifecycleCoordinator.HandleSessionSwitch(
            SessionSwitchReason.SessionLogon,
            locked => calls.Add($"locked:{locked}"),
            () => calls.Add("sampling"),
            suspended => calls.Add($"suspended:{suspended}"),
            () => calls.Add("reapply"));

        Assert.Equal(["sampling"], calls);
    }

    [Theory]
    [InlineData(LayerStrategy.TopMost, WindowLayerMode.AlwaysOnTop)]
    [InlineData(LayerStrategy.BottomMost, WindowLayerMode.OnDesktop)]
    [InlineData(LayerStrategy.Normal, WindowLayerMode.Normal)]
    public void layer_selection_uses_one_strategy_for_settings_window_and_tray(
        LayerStrategy strategy,
        WindowLayerMode expectedMode)
    {
        WindowLayerMode? saved = null;
        LayerStrategy? window = null;
        LayerStrategy? tray = null;

        LayerLifecycleCoordinator.ApplyLayerSelection(
            strategy,
            mode => saved = mode,
            selected => window = selected,
            selected => tray = selected);

        Assert.Equal(expectedMode, saved);
        Assert.Equal(strategy, window);
        Assert.Equal(strategy, tray);
    }
}
