using DesktopSystemMonitor.Core.Metrics;
using DesktopSystemMonitor.Core.Sampling;
using DesktopSystemMonitor.Core.Settings;

namespace DesktopSystemMonitor.App;

internal readonly record struct BatteryChargeProcessResult(
    AppSettings Settings,
    bool SettingsChanged,
    bool PersistAttempted,
    bool Persisted);

internal sealed class BatteryChargeStateCoordinator
{
    private readonly ISettingsStore _settingsStore;
    private readonly BatteryChargeLimitLearner _learner;
    private readonly Action<string, Exception?> _recordDiagnostic;
    private bool _persistenceDirty;

    internal BatteryChargeStateCoordinator(
        ISettingsStore settingsStore,
        Action<string, Exception?> recordDiagnostic)
        : this(settingsStore, new BatteryChargeLimitLearner(), recordDiagnostic)
    {
    }

    internal BatteryChargeStateCoordinator(
        ISettingsStore settingsStore,
        BatteryChargeLimitLearner learner,
        Action<string, Exception?> recordDiagnostic)
    {
        _settingsStore = settingsStore ?? throw new ArgumentNullException(nameof(settingsStore));
        _learner = learner ?? throw new ArgumentNullException(nameof(learner));
        _recordDiagnostic = recordDiagnostic ?? throw new ArgumentNullException(nameof(recordDiagnostic));
    }

    internal bool PersistenceDirty => _persistenceDirty;

    internal BatteryChargeProcessResult ProcessSnapshot(
        MetricSnapshot snapshot,
        AppSettings currentSettings,
        Action<AppSettings> replaceInMemory,
        Action<AppSettings> applySnapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(currentSettings);
        ArgumentNullException.ThrowIfNull(replaceInMemory);
        ArgumentNullException.ThrowIfNull(applySnapshot);

        BatteryChargeLearningResult learning = _learner.Observe(
            snapshot.TakenAt,
            snapshot.Battery,
            currentSettings);
        replaceInMemory(learning.Settings);

        bool attempted = false;
        bool persisted = false;
        if (learning.LearningEdge && (learning.SettingsChanged || _persistenceDirty))
        {
            (attempted, persisted) = TryPersist(
                learning.Settings,
                "battery-learning-settings-read-only",
                "battery-learning-settings-save-failure");
        }

        applySnapshot(learning.Settings);
        return new(learning.Settings, learning.SettingsChanged, attempted, persisted);
    }

    internal bool PersistSettings(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        (_, bool persisted) = TryPersist(
            settings,
            "settings-read-only",
            "settings-save-failure");
        return persisted;
    }

    internal void OnTransientReset() => _learner.OnTransientReset();

    private (bool Attempted, bool Persisted) TryPersist(
        AppSettings settings,
        string readOnlyCategory,
        string failureCategory)
    {
        if (_settingsStore.IsReadOnly)
        {
            _persistenceDirty = false;
            return (false, false);
        }

        try
        {
            _settingsStore.Save(settings);
            _persistenceDirty = false;
            return (true, true);
        }
        catch (InvalidOperationException ex)
        {
            _persistenceDirty = false;
            _recordDiagnostic(readOnlyCategory, ex);
            return (true, false);
        }
        catch (Exception ex)
        {
            _persistenceDirty = true;
            _recordDiagnostic(failureCategory, ex);
            return (true, false);
        }
    }
}
