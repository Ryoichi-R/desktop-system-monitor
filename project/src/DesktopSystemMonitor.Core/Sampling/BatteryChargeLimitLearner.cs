using DesktopSystemMonitor.Core.Metrics;
using DesktopSystemMonitor.Core.Settings;

namespace DesktopSystemMonitor.Core.Sampling;

public readonly record struct BatteryChargeLearningResult(
    AppSettings Settings,
    bool SettingsChanged,
    bool LearningEdge);

public sealed class BatteryChargeLimitLearner
{
    private static readonly TimeSpan MinimumContinuousDischarge = TimeSpan.FromSeconds(30);
    private BatteryPowerState? _lastPowerState;
    private DateTimeOffset? _dischargeStartedAt;
    private double _lowestDischargePercent = double.NaN;
    private double? _lastStopPercent;
    private bool _sessionChargingObserved;
    private bool _requiresDischargeBeforeRearm;
    private bool _rearmReady;
    private bool _firstObservation = true;

    public BatteryChargeLearningResult Observe(
        DateTimeOffset takenAt,
        BatterySnapshot snapshot,
        AppSettings currentSettings)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(currentSettings);

        AppSettings settings = currentSettings.Normalized();
        bool changedByNormalization = settings != currentSettings;
        bool learningEdge = false;

        if (snapshot.PowerState == BatteryPowerState.Absent)
        {
            AppSettings cleared = ClearLearning(settings);
            bool absentChanged = cleared != currentSettings;
            ResetAll(requireDischarge: false);
            _firstObservation = false;
            return new(cleared, absentChanged, absentChanged);
        }

        if (snapshot.PowerState is BatteryPowerState.Unknown or BatteryPowerState.WarmingUp ||
            snapshot.Status == MetricStatus.Unavailable)
        {
            bool hasMeaningfulObservation = !_firstObservation;
            ClearTransient(requireDischarge: hasMeaningfulObservation);
            _lastPowerState = snapshot.PowerState;
            _firstObservation = !hasMeaningfulObservation;
            return new(settings, changedByNormalization, false);
        }

        bool invalidatedLearning = ShouldInvalidateLearnedTarget(snapshot, settings);
        if (invalidatedLearning)
        {
            settings = ClearLearning(settings);
            learningEdge = true;
        }

        if (_firstObservation && snapshot.PowerState == BatteryPowerState.Discharging)
        {
            int? reference = SelectSessionReference(settings, snapshot.Percent);
            if (reference is not null)
            {
                _lastStopPercent = reference;
                _requiresDischargeBeforeRearm = true;
            }
        }

        switch (snapshot.PowerState)
        {
            case BatteryPowerState.Discharging:
                ObserveDischarge(takenAt, snapshot.Percent);
                _sessionChargingObserved = false;
                break;

            case BatteryPowerState.Charging:
                BeginChargingSessionIfEligible(snapshot.Percent, settings);
                ClearDischargeTracking();
                break;

            case BatteryPowerState.AcConnected:
                if (_firstObservation && !invalidatedLearning &&
                    SelectMatchingStoredReference(settings, snapshot.Percent) is not null)
                {
                    _lastStopPercent = snapshot.Percent;
                    _requiresDischargeBeforeRearm = true;
                }
                if (_lastPowerState == BatteryPowerState.Charging &&
                    _sessionChargingObserved &&
                    !invalidatedLearning &&
                    TryRoundLearningPercent(snapshot.Percent, out int stopPercent))
                {
                    settings = ApplyStopObservation(settings, stopPercent);
                    _lastStopPercent = snapshot.Percent;
                    _sessionChargingObserved = false;
                    _requiresDischargeBeforeRearm = true;
                    _rearmReady = false;
                    learningEdge = true;
                }
                ClearDischargeTracking();
                break;

            default:
                ClearDischargeTracking();
                break;
        }

        _lastPowerState = snapshot.PowerState;
        _firstObservation = false;
        AppSettings normalized = settings.Normalized();
        bool changed = changedByNormalization || normalized != currentSettings;
        return new(normalized, changed, learningEdge);
    }

    public void OnTransientReset()
    {
        ClearTransient(requireDischarge: true);
        _lastPowerState = null;
        _firstObservation = false;
    }

    private void BeginChargingSessionIfEligible(double percent, AppSettings settings)
    {
        if (_sessionChargingObserved)
        {
            return;
        }

        if (_rearmReady)
        {
            _sessionChargingObserved = true;
            _requiresDischargeBeforeRearm = false;
            _rearmReady = false;
            return;
        }

        if (_requiresDischargeBeforeRearm)
        {
            return;
        }

        int? reference = SelectSessionReference(settings, percent);
        bool processStartBelowReference = _firstObservation && reference is not null &&
            double.IsFinite(percent) && percent <= reference.Value - 2;
        if (reference is null || processStartBelowReference)
        {
            _sessionChargingObserved = true;
        }
        else
        {
            _requiresDischargeBeforeRearm = true;
        }
    }

    private void ObserveDischarge(DateTimeOffset takenAt, double percent)
    {
        if (!double.IsFinite(percent))
        {
            ClearDischargeTracking();
            return;
        }

        if (_lastPowerState != BatteryPowerState.Discharging || _dischargeStartedAt is null)
        {
            _dischargeStartedAt = takenAt;
            _lowestDischargePercent = percent;
        }
        else
        {
            _lowestDischargePercent = Math.Min(_lowestDischargePercent, percent);
        }

        if (_requiresDischargeBeforeRearm &&
            _lastStopPercent is { } stopPercent &&
            takenAt - _dischargeStartedAt.Value >= MinimumContinuousDischarge &&
            _lowestDischargePercent <= stopPercent - 2)
        {
            _rearmReady = true;
        }
    }

    private static AppSettings ApplyStopObservation(AppSettings settings, int observed)
    {
        int? learned = settings.LearnedBatteryChargeTargetPercent;
        int? candidate = settings.BatteryChargeTargetCandidatePercent;
        if (learned is null)
        {
            if (candidate is null)
            {
                return settings with { BatteryChargeTargetCandidatePercent = observed };
            }
            return Math.Abs(candidate.Value - observed) <= 1
                ? settings with
                {
                    LearnedBatteryChargeTargetPercent = Math.Min(candidate.Value, observed),
                    BatteryChargeTargetCandidatePercent = null,
                }
                : settings with { BatteryChargeTargetCandidatePercent = observed };
        }

        if (Math.Abs(learned.Value - observed) <= 1)
        {
            return settings with { BatteryChargeTargetCandidatePercent = null };
        }
        if (candidate is not null && Math.Abs(candidate.Value - observed) <= 1)
        {
            return settings with
            {
                LearnedBatteryChargeTargetPercent = Math.Min(candidate.Value, observed),
                BatteryChargeTargetCandidatePercent = null,
            };
        }
        return settings with { BatteryChargeTargetCandidatePercent = observed };
    }

    private static bool ShouldInvalidateLearnedTarget(BatterySnapshot snapshot, AppSettings settings) =>
        settings.LearnedBatteryChargeTargetPercent is { } learned &&
        snapshot.PowerState is BatteryPowerState.Charging or BatteryPowerState.AcConnected &&
        double.IsFinite(snapshot.Percent) && snapshot.Percent >= learned + 2;

    private static int? SelectSessionReference(AppSettings settings, double percent)
    {
        int? learned = settings.LearnedBatteryChargeTargetPercent;
        int? candidate = settings.BatteryChargeTargetCandidatePercent;
        if (learned is null || candidate is null || !double.IsFinite(percent))
        {
            return candidate ?? learned;
        }

        int[] references = [learned.Value, candidate.Value];
        int[] notBelowCurrent = references.Where(reference => reference >= percent).ToArray();
        return (notBelowCurrent.Length > 0 ? notBelowCurrent : references)
            .OrderBy(reference => Math.Abs(reference - percent))
            .First();
    }

    private static int? SelectMatchingStoredReference(AppSettings settings, double percent)
    {
        if (!double.IsFinite(percent))
        {
            return null;
        }

        int?[] references =
        [
            settings.LearnedBatteryChargeTargetPercent,
            settings.BatteryChargeTargetCandidatePercent,
        ];
        int matched = references
            .Where(reference => reference is not null)
            .Select(reference => reference!.Value)
            .OrderBy(reference => Math.Abs(reference - percent))
            .FirstOrDefault(reference => Math.Abs(reference - percent) <= 1, -1);
        return matched >= 0 ? matched : null;
    }

    private static bool TryRoundLearningPercent(double percent, out int rounded)
    {
        rounded = 0;
        if (!double.IsFinite(percent))
        {
            return false;
        }
        rounded = checked((int)Math.Round(percent, MidpointRounding.AwayFromZero));
        return rounded is >= 50 and <= 99;
    }

    private static AppSettings ClearLearning(AppSettings settings) => settings with
    {
        LearnedBatteryChargeTargetPercent = null,
        BatteryChargeTargetCandidatePercent = null,
    };

    private void ClearTransient(bool requireDischarge)
    {
        _sessionChargingObserved = false;
        _requiresDischargeBeforeRearm = requireDischarge;
        _rearmReady = false;
        ClearDischargeTracking();
    }

    private void ClearDischargeTracking()
    {
        _dischargeStartedAt = null;
        _lowestDischargePercent = double.NaN;
    }

    private void ResetAll(bool requireDischarge)
    {
        _lastPowerState = null;
        _lastStopPercent = null;
        ClearTransient(requireDischarge);
    }
}
