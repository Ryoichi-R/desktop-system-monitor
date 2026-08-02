using System.Collections.Immutable;
using DesktopSystemMonitor.Core.Formatting;
using DesktopSystemMonitor.Core.Settings;
using Xunit;

namespace DesktopSystemMonitor.App.Tests;

public sealed class SettingsEditMergeTests
{
    [Fact]
    public void every_typed_draft_property_has_a_matching_settings_property()
    {
        foreach (var draftProperty in typeof(SettingsDraft).GetProperties())
        {
            Assert.NotNull(typeof(AppSettings).GetProperty(draftProperty.Name));
        }
    }

    [Fact]
    public void merge_applies_every_dialog_field_and_preserves_runtime_owned_fields()
    {
        var current = new AppSettings
        {
            Opacity = 0.7,
            BackgroundEnabled = false,
            BackgroundOpacity = 0.7,
            HideBackgroundBehindWindows = false,
            BackgroundColor = "#AA010203",
            SavedMonitorDeviceName = "DISPLAY-CURRENT",
            SavedRightEdgeDip = 123,
            SavedTopEdgeDip = 456,
            SavedMonitorDpi = 144,
            LearnedBatteryChargeTargetPercent = 80,
            BatteryChargeTargetCandidatePercent = 60,
        }.Normalized();
        var edited = new AppSettings
        {
            SamplingIntervalSeconds = 2.5,
            UiScalePercent = 125,
            LayerMode = WindowLayerMode.Normal,
            NetworkUnitSystem = RateUnitSystem.DecimalBits,
            ClickThrough = false,
            AutoHideOnFullScreen = false,
            StartWithWindows = true,
            DiagnosticLoggingEnabled = true,
            ShowRecentPeaks = true,
            ShowNetworkPeaks = true,
            BatteryChargeTargetPercent = 90,
            FontFamilyName = "Consolas",
            ForegroundColor = "#FF010101",
            MutedColor = "#FF020202",
            AccentColor = "#FF030303",
            RxAccentColor = "#FF040404",
            TxAccentColor = "#FF050505",
            PinnedGpuLuidHex = "ABCDEF",
            SelectedNetworkAdapterLuids = ImmutableArray.Create<ulong>(10, 20),
            Opacity = 0.3,
            BackgroundEnabled = true,
            BackgroundOpacity = 0.3,
            BackgroundFillMode = BackgroundFillMode.EdgeFade,
            BackgroundEdgeFadePercent = 31,
            HideBackgroundBehindWindows = true,
            BackgroundColor = "#AA999999",
            SavedMonitorDeviceName = "DISPLAY-STALE",
            SavedRightEdgeDip = 999,
            SavedTopEdgeDip = 999,
            SavedMonitorDpi = 96,
        }.Normalized();

        SettingsDraft draft = SettingsDraft.FromSettings(edited);
        AppSettings merged = SettingsEditMerge.Merge(current, draft);

        foreach (var draftProperty in typeof(SettingsDraft).GetProperties())
        {
            var settingsProperty = typeof(AppSettings).GetProperty(draftProperty.Name)!;
            Assert.Equal(draftProperty.GetValue(draft), settingsProperty.GetValue(merged));
        }
        Assert.Equal(current.Opacity, merged.Opacity);
        Assert.Equal(edited.BackgroundEnabled, merged.BackgroundEnabled);
        Assert.Equal(edited.BackgroundOpacity, merged.BackgroundOpacity);
        Assert.Equal(edited.BackgroundFillMode, merged.BackgroundFillMode);
        Assert.Equal(edited.BackgroundEdgeFadePercent, merged.BackgroundEdgeFadePercent);
        Assert.Equal(edited.HideBackgroundBehindWindows, merged.HideBackgroundBehindWindows);
        Assert.Equal("#FF999999", merged.BackgroundColor);
        Assert.Equal(current.LearnedBatteryChargeTargetPercent, merged.LearnedBatteryChargeTargetPercent);
        Assert.Equal(current.BatteryChargeTargetCandidatePercent, merged.BatteryChargeTargetCandidatePercent);
    }

    [Fact]
    public void merge_synchronizes_legacy_temperature_to_the_override_or()
    {
        var current = new AppSettings { ShowTemperatures = false };
        SettingsDraft edited = SettingsDraft.FromSettings(new AppSettings
        {
            ShowCpuTemperature = false,
            ShowGpuTemperature = true,
        });

        AppSettings merged = SettingsEditMerge.Merge(current, edited);

        Assert.False(merged.ShowCpuTemperature);
        Assert.True(merged.ShowGpuTemperature);
        Assert.True(merged.ShowTemperatures);
    }

    [Fact]
    public void reset_request_clears_the_latest_runtime_learning_only_on_save()
    {
        var latest = new AppSettings
        {
            LearnedBatteryChargeTargetPercent = 80,
            BatteryChargeTargetCandidatePercent = 60,
        }.Normalized();
        SettingsDraft staleDialogDraft = SettingsDraft.FromSettings(new AppSettings
        {
            BatteryChargeTargetPercent = 90,
        });

        AppSettings savedWithoutReset = SettingsEditMerge.Merge(latest, staleDialogDraft);
        AppSettings savedWithReset = SettingsEditMerge.Merge(
            latest,
            staleDialogDraft,
            resetBatteryChargeLearning: true);

        Assert.Equal(80, savedWithoutReset.LearnedBatteryChargeTargetPercent);
        Assert.Equal(60, savedWithoutReset.BatteryChargeTargetCandidatePercent);
        Assert.Null(savedWithReset.LearnedBatteryChargeTargetPercent);
        Assert.Null(savedWithReset.BatteryChargeTargetCandidatePercent);
        Assert.Equal(90, savedWithReset.BatteryChargeTargetPercent);
    }
}
