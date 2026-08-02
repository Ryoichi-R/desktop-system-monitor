using DesktopSystemMonitor.Core.Metrics;
using DesktopSystemMonitor.Core.Sampling;
using DesktopSystemMonitor.Core.Settings;
using Xunit;

namespace DesktopSystemMonitor.Core.Tests.Sampling;

public sealed class BatteryChargeLimitLearnerTests
{
    [Fact]
    public void two_independent_stops_promote_the_smaller_observation()
    {
        var learner = new BatteryChargeLimitLearner();
        DateTimeOffset start = DateTimeOffset.UtcNow;
        AppSettings settings = new AppSettings().Normalized();

        settings = Observe(learner, settings, start, BatteryPowerState.Charging, 70).Settings;
        BatteryChargeLearningResult first = Observe(learner, settings, start.AddMinutes(1), BatteryPowerState.AcConnected, 80);
        Assert.Equal(80, first.Settings.BatteryChargeTargetCandidatePercent);
        Assert.Null(first.Settings.LearnedBatteryChargeTargetPercent);

        settings = Rearm(learner, first.Settings, start.AddMinutes(2), 80, 78);
        settings = Observe(learner, settings, start.AddMinutes(3), BatteryPowerState.Charging, 78).Settings;
        BatteryChargeLearningResult second = Observe(learner, settings, start.AddMinutes(4), BatteryPowerState.AcConnected, 79);

        Assert.Equal(79, second.Settings.LearnedBatteryChargeTargetPercent);
        Assert.Null(second.Settings.BatteryChargeTargetCandidatePercent);
    }

    [Fact]
    public void short_discharge_does_not_create_an_independent_session()
    {
        var learner = new BatteryChargeLimitLearner();
        DateTimeOffset start = DateTimeOffset.UtcNow;
        AppSettings settings = Observe(learner, new AppSettings(), start, BatteryPowerState.Charging, 70).Settings;
        settings = Observe(learner, settings, start.AddMinutes(1), BatteryPowerState.AcConnected, 80).Settings;
        settings = Observe(learner, settings, start.AddMinutes(2), BatteryPowerState.Discharging, 79).Settings;
        settings = Observe(learner, settings, start.AddMinutes(2).AddSeconds(5), BatteryPowerState.Discharging, 77).Settings;
        settings = Observe(learner, settings, start.AddMinutes(3), BatteryPowerState.Charging, 77).Settings;
        BatteryChargeLearningResult result = Observe(learner, settings, start.AddMinutes(4), BatteryPowerState.AcConnected, 80);

        Assert.Equal(80, result.Settings.BatteryChargeTargetCandidatePercent);
        Assert.Null(result.Settings.LearnedBatteryChargeTargetPercent);
        Assert.False(result.LearningEdge);
    }

    [Fact]
    public void learned_target_coexists_with_replacement_candidate_until_confirmed()
    {
        var learner = new BatteryChargeLimitLearner();
        DateTimeOffset start = DateTimeOffset.UtcNow;
        AppSettings settings = new AppSettings { LearnedBatteryChargeTargetPercent = 80 }.Normalized();

        settings = Observe(learner, settings, start, BatteryPowerState.Charging, 70).Settings;
        settings = Observe(learner, settings, start.AddMinutes(1), BatteryPowerState.AcConnected, 60).Settings;
        Assert.Equal(80, settings.LearnedBatteryChargeTargetPercent);
        Assert.Equal(60, settings.BatteryChargeTargetCandidatePercent);

        settings = Rearm(learner, settings, start.AddMinutes(2), 60, 58);
        settings = Observe(learner, settings, start.AddMinutes(3), BatteryPowerState.Charging, 58).Settings;
        settings = Observe(learner, settings, start.AddMinutes(4), BatteryPowerState.AcConnected, 61).Settings;

        Assert.Equal(60, settings.LearnedBatteryChargeTargetPercent);
        Assert.Null(settings.BatteryChargeTargetCandidatePercent);
    }

    [Fact]
    public void exceeding_learned_target_invalidates_learning()
    {
        var learner = new BatteryChargeLimitLearner();
        var settings = new AppSettings
        {
            LearnedBatteryChargeTargetPercent = 80,
            BatteryChargeTargetCandidatePercent = 60,
        }.Normalized();

        BatteryChargeLearningResult result = Observe(
            learner,
            settings,
            DateTimeOffset.UtcNow,
            BatteryPowerState.Charging,
            82);

        Assert.Null(result.Settings.LearnedBatteryChargeTargetPercent);
        Assert.Null(result.Settings.BatteryChargeTargetCandidatePercent);
        Assert.True(result.LearningEdge);
    }

    [Fact]
    public void transient_reset_prevents_ac_state_from_becoming_a_stop()
    {
        var learner = new BatteryChargeLimitLearner();
        DateTimeOffset start = DateTimeOffset.UtcNow;
        AppSettings settings = Observe(learner, new AppSettings(), start, BatteryPowerState.Charging, 70).Settings;

        learner.OnTransientReset();
        BatteryChargeLearningResult result = Observe(
            learner,
            settings,
            start.AddMinutes(1),
            BatteryPowerState.AcConnected,
            80);

        Assert.Null(result.Settings.BatteryChargeTargetCandidatePercent);
        Assert.False(result.LearningEdge);
    }

    [Fact]
    public void startup_warmup_does_not_disqualify_the_first_charging_session()
    {
        var learner = new BatteryChargeLimitLearner();
        DateTimeOffset start = DateTimeOffset.UtcNow;
        AppSettings settings = learner.Observe(start, BatterySnapshot.Warmup(), new AppSettings()).Settings;
        settings = Observe(learner, settings, start.AddSeconds(1), BatteryPowerState.Charging, 70).Settings;

        BatteryChargeLearningResult result = Observe(
            learner,
            settings,
            start.AddMinutes(1),
            BatteryPowerState.AcConnected,
            80);

        Assert.Equal(80, result.Settings.BatteryChargeTargetCandidatePercent);
    }

    [Fact]
    public void ac_sample_that_invalidates_learning_is_not_reused_as_a_candidate()
    {
        var learner = new BatteryChargeLimitLearner();
        DateTimeOffset start = DateTimeOffset.UtcNow;
        AppSettings settings = new AppSettings { LearnedBatteryChargeTargetPercent = 80 }.Normalized();
        settings = Observe(learner, settings, start, BatteryPowerState.Charging, 70).Settings;

        BatteryChargeLearningResult result = Observe(
            learner,
            settings,
            start.AddMinutes(1),
            BatteryPowerState.AcConnected,
            82);

        Assert.Null(result.Settings.LearnedBatteryChargeTargetPercent);
        Assert.Null(result.Settings.BatteryChargeTargetCandidatePercent);
    }

    [Fact]
    public void process_starting_during_discharge_can_rearm_a_saved_candidate()
    {
        var learner = new BatteryChargeLimitLearner();
        DateTimeOffset start = DateTimeOffset.UtcNow;
        AppSettings settings = new AppSettings { BatteryChargeTargetCandidatePercent = 80 }.Normalized();
        settings = Observe(learner, settings, start, BatteryPowerState.Discharging, 79).Settings;
        settings = Observe(learner, settings, start.AddSeconds(31), BatteryPowerState.Discharging, 78).Settings;
        settings = Observe(learner, settings, start.AddSeconds(32), BatteryPowerState.Charging, 78).Settings;

        BatteryChargeLearningResult result = Observe(
            learner,
            settings,
            start.AddMinutes(1),
            BatteryPowerState.AcConnected,
            80);

        Assert.Equal(80, result.Settings.LearnedBatteryChargeTargetPercent);
        Assert.Null(result.Settings.BatteryChargeTargetCandidatePercent);
    }

    [Fact]
    public void restart_at_saved_candidate_rearms_after_a_real_discharge_session()
    {
        var learner = new BatteryChargeLimitLearner();
        DateTimeOffset start = DateTimeOffset.UtcNow;
        AppSettings settings = new AppSettings { BatteryChargeTargetCandidatePercent = 80 }.Normalized();
        settings = Observe(learner, settings, start, BatteryPowerState.AcConnected, 80).Settings;
        settings = Observe(learner, settings, start.AddSeconds(1), BatteryPowerState.Discharging, 79).Settings;
        settings = Observe(learner, settings, start.AddSeconds(32), BatteryPowerState.Discharging, 78).Settings;
        settings = Observe(learner, settings, start.AddSeconds(33), BatteryPowerState.Charging, 78).Settings;

        BatteryChargeLearningResult result = Observe(
            learner,
            settings,
            start.AddMinutes(1),
            BatteryPowerState.AcConnected,
            80);

        Assert.Equal(80, result.Settings.LearnedBatteryChargeTargetPercent);
        Assert.Null(result.Settings.BatteryChargeTargetCandidatePercent);
    }

    [Theory]
    [InlineData(80, 78, 80, 80)]
    [InlineData(79, 77, 80, 80)]
    [InlineData(60, 58, 61, 60)]
    [InlineData(61, 58, 60, 60)]
    public void coexistence_selects_the_reference_matching_the_restart_percent(
        double restartPercent,
        double dischargedPercent,
        double secondStopPercent,
        int expectedLearned)
    {
        var learner = new BatteryChargeLimitLearner();
        DateTimeOffset start = DateTimeOffset.UtcNow;
        AppSettings settings = new AppSettings
        {
            LearnedBatteryChargeTargetPercent = 80,
            BatteryChargeTargetCandidatePercent = 60,
        }.Normalized();
        settings = Observe(learner, settings, start, BatteryPowerState.AcConnected, restartPercent).Settings;
        settings = Observe(learner, settings, start.AddSeconds(1), BatteryPowerState.Discharging, restartPercent - 1).Settings;
        settings = Observe(learner, settings, start.AddSeconds(32), BatteryPowerState.Discharging, dischargedPercent).Settings;
        settings = Observe(learner, settings, start.AddSeconds(33), BatteryPowerState.Charging, dischargedPercent).Settings;

        BatteryChargeLearningResult result = Observe(
            learner,
            settings,
            start.AddMinutes(1),
            BatteryPowerState.AcConnected,
            secondStopPercent);

        Assert.Equal(expectedLearned, result.Settings.LearnedBatteryChargeTargetPercent);
        Assert.Null(result.Settings.BatteryChargeTargetCandidatePercent);
    }

    [Fact]
    public void repeated_charging_samples_keep_the_same_active_session()
    {
        var learner = new BatteryChargeLimitLearner();
        DateTimeOffset start = DateTimeOffset.UtcNow;
        AppSettings settings = Observe(learner, new AppSettings(), start, BatteryPowerState.Charging, 70).Settings;
        settings = Observe(learner, settings, start.AddSeconds(1), BatteryPowerState.Charging, 71).Settings;

        BatteryChargeLearningResult result = Observe(
            learner,
            settings,
            start.AddMinutes(1),
            BatteryPowerState.AcConnected,
            80);

        Assert.Equal(80, result.Settings.BatteryChargeTargetCandidatePercent);
    }

    [Fact]
    public void matching_learned_stop_clears_a_stale_replacement_candidate()
    {
        var learner = new BatteryChargeLimitLearner();
        DateTimeOffset start = DateTimeOffset.UtcNow;
        AppSettings settings = new AppSettings
        {
            LearnedBatteryChargeTargetPercent = 80,
            BatteryChargeTargetCandidatePercent = 60,
        }.Normalized();
        settings = Observe(learner, settings, start, BatteryPowerState.Charging, 50).Settings;

        BatteryChargeLearningResult result = Observe(
            learner,
            settings,
            start.AddMinutes(1),
            BatteryPowerState.AcConnected,
            80);

        Assert.Equal(80, result.Settings.LearnedBatteryChargeTargetPercent);
        Assert.Null(result.Settings.BatteryChargeTargetCandidatePercent);
    }

    [Fact]
    public void non_finite_discharge_percent_never_rearms_learning()
    {
        var learner = new BatteryChargeLimitLearner();
        DateTimeOffset start = DateTimeOffset.UtcNow;
        AppSettings settings = Observe(learner, new AppSettings(), start, BatteryPowerState.Charging, 70).Settings;
        settings = Observe(learner, settings, start.AddMinutes(1), BatteryPowerState.AcConnected, 80).Settings;
        settings = Observe(learner, settings, start.AddMinutes(2), BatteryPowerState.Discharging, double.NaN).Settings;
        settings = Observe(learner, settings, start.AddMinutes(3), BatteryPowerState.Charging, 70).Settings;
        BatteryChargeLearningResult result = Observe(
            learner,
            settings,
            start.AddMinutes(4),
            BatteryPowerState.AcConnected,
            80);

        Assert.Null(result.Settings.LearnedBatteryChargeTargetPercent);
        Assert.Equal(80, result.Settings.BatteryChargeTargetCandidatePercent);
    }

    [Fact]
    public void absent_battery_clears_persisted_learning_but_unknown_does_not()
    {
        var learner = new BatteryChargeLimitLearner();
        var settings = new AppSettings
        {
            LearnedBatteryChargeTargetPercent = 80,
            BatteryChargeTargetCandidatePercent = 60,
        }.Normalized();
        DateTimeOffset start = DateTimeOffset.UtcNow;

        BatteryChargeLearningResult unknown = learner.Observe(
            start,
            Snapshot(BatteryPowerState.Unknown, double.NaN) with
            {
                Status = MetricStatus.Unavailable,
                BatteryPresent = false,
            },
            settings);
        Assert.Equal(80, unknown.Settings.LearnedBatteryChargeTargetPercent);
        Assert.Equal(60, unknown.Settings.BatteryChargeTargetCandidatePercent);

        BatteryChargeLearningResult absent = learner.Observe(start.AddSeconds(1), BatterySnapshot.Absent(), unknown.Settings);
        Assert.Null(absent.Settings.LearnedBatteryChargeTargetPercent);
        Assert.Null(absent.Settings.BatteryChargeTargetCandidatePercent);
    }

    private static AppSettings Rearm(
        BatteryChargeLimitLearner learner,
        AppSettings settings,
        DateTimeOffset start,
        double stopPercent,
        double lowerPercent)
    {
        settings = Observe(learner, settings, start, BatteryPowerState.Discharging, stopPercent - 1).Settings;
        return Observe(learner, settings, start.AddSeconds(31), BatteryPowerState.Discharging, lowerPercent).Settings;
    }

    private static BatteryChargeLearningResult Observe(
        BatteryChargeLimitLearner learner,
        AppSettings settings,
        DateTimeOffset takenAt,
        BatteryPowerState state,
        double percent) => learner.Observe(takenAt, Snapshot(state, percent), settings);

    private static BatterySnapshot Snapshot(BatteryPowerState state, double percent) => new()
    {
        Status = MetricStatus.Ok,
        PowerState = state,
        BatteryPresent = state != BatteryPowerState.Absent,
        Percent = percent,
        RemainingCapacityMilliwattHours = 30_000,
        RateMilliwatts = state == BatteryPowerState.Charging ? 12_000 : state == BatteryPowerState.Discharging ? -8_000 : 0,
        WindowsEstimatedTime = null,
    };
}
