#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using DesktopSystemMonitor.Core.Metrics;
using DesktopSystemMonitor.Core.Settings;
using Xunit;

namespace DesktopSystemMonitor.App.Tests;

public sealed class BatteryChargeStateCoordinatorTests
{
    [Fact]
    public void learning_edge_replaces_memory_then_persists_then_applies_same_settings()
    {
        var events = new List<string>();
        var store = new FakeSettingsStore { OnSave = _ => events.Add("save") };
        var coordinator = new BatteryChargeStateCoordinator(store, (_, _) => { });
        AppSettings current = new AppSettings().Normalized();
        DateTimeOffset start = DateTimeOffset.UtcNow;
        coordinator.ProcessSnapshot(
            Snapshot(start, BatteryPowerState.Charging, 70),
            current,
            settings => current = settings,
            _ => { });
        events.Clear();

        BatteryChargeProcessResult result = coordinator.ProcessSnapshot(
            Snapshot(start.AddMinutes(1), BatteryPowerState.AcConnected, 80),
            current,
            settings =>
            {
                events.Add("replace");
                current = settings;
            },
            settings =>
            {
                events.Add("apply");
                Assert.Same(current, settings);
            });

        Assert.Equal(["replace", "save", "apply"], events);
        Assert.Equal(80, current.BatteryChargeTargetCandidatePercent);
        Assert.True(result.PersistAttempted);
        Assert.True(result.Persisted);
    }

    [Fact]
    public void save_failure_keeps_new_in_memory_target_and_marks_dirty()
    {
        var events = new List<string>();
        var store = new FakeSettingsStore
        {
            SaveException = new IOException("temporary"),
            OnSave = _ => events.Add("save"),
        };
        var diagnostics = new List<string>();
        var coordinator = new BatteryChargeStateCoordinator(store, (category, _) => diagnostics.Add(category));
        AppSettings current = new AppSettings { BatteryChargeTargetCandidatePercent = 80 }.Normalized();
        DateTimeOffset start = DateTimeOffset.UtcNow;
        coordinator.ProcessSnapshot(Snapshot(start, BatteryPowerState.Charging, 70), current, value => current = value, _ => { });
        events.Clear();

        coordinator.ProcessSnapshot(
            Snapshot(start.AddMinutes(1), BatteryPowerState.AcConnected, 79),
            current,
            settings =>
            {
                events.Add("replace");
                current = settings;
            },
            settings =>
            {
                events.Add("apply");
                Assert.Equal(79, settings.EffectiveBatteryChargeTargetPercent);
            });

        Assert.Equal(["replace", "save", "apply"], events);
        Assert.True(coordinator.PersistenceDirty);
        Assert.Contains("battery-learning-settings-save-failure", diagnostics);

        store.SaveException = null;
        Assert.True(coordinator.PersistSettings(current));
        Assert.False(coordinator.PersistenceDirty);
    }

    [Fact]
    public void read_only_store_is_not_called_and_runtime_learning_still_applies()
    {
        var store = new FakeSettingsStore { IsReadOnly = true };
        var coordinator = new BatteryChargeStateCoordinator(store, (_, _) => { });
        AppSettings current = new AppSettings().Normalized();
        DateTimeOffset start = DateTimeOffset.UtcNow;
        coordinator.ProcessSnapshot(Snapshot(start, BatteryPowerState.Charging, 70), current, value => current = value, _ => { });

        coordinator.ProcessSnapshot(
            Snapshot(start.AddMinutes(1), BatteryPowerState.AcConnected, 80),
            current,
            value => current = value,
            settings => Assert.Equal(80, settings.BatteryChargeTargetCandidatePercent));

        Assert.Equal(0, store.SaveCount);
        Assert.False(coordinator.PersistenceDirty);
    }

    private static MetricSnapshot Snapshot(DateTimeOffset takenAt, BatteryPowerState state, double percent) =>
        MetricSnapshot.Warmup(takenAt) with
        {
            Battery = new BatterySnapshot
            {
                Status = MetricStatus.Ok,
                PowerState = state,
                BatteryPresent = true,
                Percent = percent,
                RemainingCapacityMilliwattHours = 30_000,
                RateMilliwatts = state == BatteryPowerState.Charging ? 12_000 : 0,
                WindowsEstimatedTime = null,
            },
        };

    private sealed class FakeSettingsStore : ISettingsStore
    {
        public bool IsReadOnly { get; init; }
        public int SaveCount { get; private set; }
        public Exception? SaveException { get; set; }
        public Action<AppSettings>? OnSave { get; init; }

        public AppSettings Load() => new AppSettings().Normalized();

        public void Save(AppSettings settings)
        {
            SaveCount++;
            OnSave?.Invoke(settings);
            if (SaveException is not null)
            {
                throw SaveException;
            }
        }
    }
}
