using System;
using System.Collections.Generic;
using DesktopSystemMonitor.App.Startup;
using DesktopSystemMonitor.Core.Settings;
using Xunit;

namespace DesktopSystemMonitor.App.Tests;

public sealed class DisplayModeChangeCoordinatorTests
{
    [Fact]
    public void successful_change_persists_before_replacing_and_applying_runtime_state()
    {
        AppSettings current = new AppSettings { DisplayMode = WidgetDisplayMode.Standard }.Normalized();
        AppSettings inMemory = current;
        var calls = new List<string>();

        DisplayModeChangeResult result = DisplayModeChangeCoordinator.Apply(
            current,
            WidgetDisplayMode.Reduced,
            editingAllowed: true,
            candidate =>
            {
                calls.Add($"save:{candidate.DisplayMode}");
                Assert.Same(current, inMemory);
                return true;
            },
            candidate =>
            {
                calls.Add($"replace:{candidate.DisplayMode}");
                inMemory = candidate;
            },
            candidate =>
            {
                calls.Add($"runtime:{candidate.DisplayMode}");
                Assert.Same(candidate, inMemory);
            },
            mode => calls.Add($"tray:{mode}"));

        Assert.Equal(DisplayModeChangeResult.Applied, result);
        Assert.Equal(WidgetDisplayMode.Reduced, inMemory.DisplayMode);
        Assert.Equal(
            ["save:Reduced", "replace:Reduced", "runtime:Reduced", "tray:Reduced"],
            calls);
    }

    [Fact]
    public void save_failure_restores_tray_without_mutating_or_applying_runtime_state()
    {
        AppSettings current = new AppSettings { DisplayMode = WidgetDisplayMode.Standard }.Normalized();
        AppSettings inMemory = current;
        var calls = new List<string>();

        DisplayModeChangeResult result = DisplayModeChangeCoordinator.Apply(
            current,
            WidgetDisplayMode.Reduced,
            editingAllowed: true,
            candidate =>
            {
                calls.Add($"save:{candidate.DisplayMode}");
                return false;
            },
            candidate => inMemory = candidate,
            _ => calls.Add("runtime"),
            mode => calls.Add($"tray:{mode}"));

        Assert.Equal(DisplayModeChangeResult.SaveFailed, result);
        Assert.Same(current, inMemory);
        Assert.Equal(["save:Reduced", "tray:Standard"], calls);
    }

    [Fact]
    public void editing_rejection_only_restores_the_current_tray_selection()
    {
        AppSettings current = new AppSettings { DisplayMode = WidgetDisplayMode.Standard }.Normalized();
        var calls = new List<string>();

        DisplayModeChangeResult result = DisplayModeChangeCoordinator.Apply(
            current,
            WidgetDisplayMode.Reduced,
            editingAllowed: false,
            _ =>
            {
                calls.Add("save");
                return true;
            },
            _ => calls.Add("replace"),
            _ => calls.Add("runtime"),
            mode => calls.Add($"tray:{mode}"));

        Assert.Equal(DisplayModeChangeResult.EditingRejected, result);
        Assert.Equal(["tray:Standard"], calls);
    }

    [Fact]
    public void selecting_the_current_mode_is_a_no_op_that_resynchronizes_the_tray()
    {
        AppSettings current = new AppSettings { DisplayMode = WidgetDisplayMode.Reduced }.Normalized();
        var calls = new List<string>();

        DisplayModeChangeResult result = DisplayModeChangeCoordinator.Apply(
            current,
            WidgetDisplayMode.Reduced,
            editingAllowed: true,
            _ => throw new InvalidOperationException("Save must not run."),
            _ => throw new InvalidOperationException("Replace must not run."),
            _ => throw new InvalidOperationException("Runtime apply must not run."),
            mode => calls.Add($"tray:{mode}"));

        Assert.Equal(DisplayModeChangeResult.Unchanged, result);
        Assert.Equal(["tray:Reduced"], calls);
    }
}
