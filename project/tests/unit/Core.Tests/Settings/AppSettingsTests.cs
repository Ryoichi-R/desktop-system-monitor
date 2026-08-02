using DesktopSystemMonitor.Core.Settings;
using Xunit;

namespace DesktopSystemMonitor.Core.Tests.Settings;

public sealed class AppSettingsTests
{
    [Theory]
    [InlineData("#123456")]
    [InlineData("#AA123456")]
    [InlineData(" #abcdef ")]
    public void valid_colors_are_accepted(string value)
    {
        Assert.True(AppSettings.IsValidColor(value));
    }

    [Theory]
    [InlineData("")]
    [InlineData("123456")]
    [InlineData("#12345")]
    [InlineData("#GG123456")]
    public void invalid_colors_are_rejected(string value)
    {
        Assert.False(AppSettings.IsValidColor(value));
    }

    [Fact]
    public void optional_features_default_off_and_invalid_disk_is_normalized()
    {
        var settings = new AppSettings { SelectedPhysicalDiskNumber = -1 }.Normalized();
        Assert.False(settings.ShowDiskMetrics);
        Assert.True(settings.ShowCpuMetrics);
        Assert.True(settings.ShowGpuMetrics);
        Assert.Equal(WidgetDisplayMode.Standard, settings.DisplayMode);
        Assert.Equal(60, settings.NetworkPeakWindowSeconds);
        Assert.False(settings.ShowTemperatures);
        Assert.False(settings.ShowBatteryEstimate);
        Assert.False(settings.ShowRecentPeaks);
        Assert.Null(settings.ShowNetworkPeaks);
        Assert.False(settings.EffectiveShowNetworkPeaks);
        Assert.False(settings.EnableHighLoadProcessDetails);
        Assert.False(settings.BackgroundEnabled);
        Assert.Null(settings.BackgroundOpacity);
        Assert.Equal(BackgroundFillMode.Solid, settings.BackgroundFillMode);
        Assert.Equal(22, settings.BackgroundEdgeFadePercent);
        Assert.False(settings.HideBackgroundBehindWindows);
        Assert.Equal("#000000", settings.EffectiveBackgroundRgb);
        Assert.Equal(176d / 255d, settings.EffectiveBackgroundOpacity, 10);
        Assert.Null(settings.SelectedPhysicalDiskNumber);
        Assert.Equal(WindowPlacementMode.Preset, settings.PlacementMode);
        Assert.Equal(WindowPlacementAnchor.TopRight, settings.PlacementAnchor);
        Assert.Equal(8, settings.HorizontalMarginDip);
        Assert.Equal(8, settings.VerticalMarginDip);
    }

    [Theory]
    [InlineData(-1, 0)]
    [InlineData(30, 30)]
    [InlineData(201, 200)]
    [InlineData(double.NaN, 8)]
    public void placement_margins_are_normalized(double input, double expected)
    {
        AppSettings settings = new AppSettings
        {
            HorizontalMarginDip = input,
            VerticalMarginDip = input,
        }.Normalized();

        Assert.Equal(expected, settings.HorizontalMarginDip);
        Assert.Equal(expected, settings.VerticalMarginDip);
    }

    [Fact]
    public void unknown_display_mode_is_normalized_to_standard()
    {
        AppSettings settings = new AppSettings { DisplayMode = (WidgetDisplayMode)999 }.Normalized();

        Assert.Equal(WidgetDisplayMode.Standard, settings.DisplayMode);
    }

    [Theory]
    [InlineData("#80112233", null, 128d / 255d)]
    [InlineData("#112233", null, 1d)]
    [InlineData("#80112233", 0.25d, 0.25d)]
    [InlineData("#112233", -1d, 0d)]
    [InlineData("#112233", 2d, 1d)]
    public void background_opacity_uses_explicit_value_before_legacy_alpha(
        string color,
        double? opacity,
        double expected)
    {
        AppSettings settings = new AppSettings
        {
            BackgroundColor = color,
            BackgroundOpacity = opacity,
        }.Normalized();

        Assert.Equal("#112233", settings.EffectiveBackgroundRgb);
        Assert.Equal(expected, settings.EffectiveBackgroundOpacity, 10);
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void non_finite_explicit_background_opacity_falls_back_to_color_alpha(double value)
    {
        AppSettings settings = new AppSettings
        {
            BackgroundColor = "#40112233",
            BackgroundOpacity = value,
        }.Normalized();

        Assert.Null(settings.BackgroundOpacity);
        Assert.Equal(64d / 255d, settings.EffectiveBackgroundOpacity, 10);
    }

    [Theory]
    [InlineData(BackgroundFillMode.EdgeFade, 4, BackgroundFillMode.EdgeFade, 5)]
    [InlineData(BackgroundFillMode.EdgeFade, 30, BackgroundFillMode.EdgeFade, 30)]
    [InlineData(BackgroundFillMode.EdgeFade, 51, BackgroundFillMode.EdgeFade, 50)]
    [InlineData((BackgroundFillMode)999, double.NaN, BackgroundFillMode.Solid, 22)]
    public void background_gradient_settings_are_normalized(
        BackgroundFillMode mode,
        double fadePercent,
        BackgroundFillMode expectedMode,
        double expectedFadePercent)
    {
        AppSettings settings = new AppSettings
        {
            BackgroundFillMode = mode,
            BackgroundEdgeFadePercent = fadePercent,
        }.Normalized();

        Assert.Equal(expectedMode, settings.BackgroundFillMode);
        Assert.Equal(expectedFadePercent, settings.BackgroundEdgeFadePercent);
    }

    [Theory]
    [InlineData(9, 10)]
    [InlineData(10, 10)]
    [InlineData(30, 30)]
    [InlineData(60, 60)]
    [InlineData(61, 60)]
    public void network_peak_window_is_clamped(int input, int expected) =>
        Assert.Equal(expected, new AppSettings { NetworkPeakWindowSeconds = input }.Normalized().NetworkPeakWindowSeconds);

    [Fact]
    public void temperature_overrides_fall_back_to_legacy_value()
    {
        var inherited = new AppSettings { ShowTemperatures = true };
        Assert.True(inherited.EffectiveShowCpuTemperature);
        Assert.True(inherited.EffectiveShowGpuTemperature);
        Assert.False((inherited with { ShowCpuTemperature = false }).EffectiveShowCpuTemperature);
    }

    [Fact]
    public void network_peak_override_falls_back_to_the_legacy_peak_setting()
    {
        var inherited = new AppSettings { ShowRecentPeaks = true };

        Assert.True(inherited.EffectiveShowNetworkPeaks);
        Assert.False((inherited with { ShowNetworkPeaks = false }).EffectiveShowNetworkPeaks);
    }

    [Fact]
    public void battery_charge_target_uses_manual_then_learned_then_fallback_priority()
    {
        var fallback = new AppSettings().Normalized();
        var learned = fallback with { LearnedBatteryChargeTargetPercent = 80 };
        var manual = learned with { BatteryChargeTargetPercent = 90 };

        Assert.Equal(100, fallback.EffectiveBatteryChargeTargetPercent);
        Assert.Equal(BatteryChargeTargetSource.Fallback, fallback.EffectiveBatteryChargeTargetSource);
        Assert.Equal(80, learned.EffectiveBatteryChargeTargetPercent);
        Assert.Equal(BatteryChargeTargetSource.Learned, learned.EffectiveBatteryChargeTargetSource);
        Assert.Equal(90, manual.EffectiveBatteryChargeTargetPercent);
        Assert.Equal(BatteryChargeTargetSource.Manual, manual.EffectiveBatteryChargeTargetSource);
    }

    [Fact]
    public void battery_charge_learning_fields_are_normalized_deterministically()
    {
        AppSettings settings = new AppSettings
        {
            BatteryChargeTargetPercent = 49,
            LearnedBatteryChargeTargetPercent = 80,
            BatteryChargeTargetCandidatePercent = 79,
        }.Normalized();

        Assert.Null(settings.BatteryChargeTargetPercent);
        Assert.Equal(80, settings.LearnedBatteryChargeTargetPercent);
        Assert.Null(settings.BatteryChargeTargetCandidatePercent);

        AppSettings replacement = (settings with { BatteryChargeTargetCandidatePercent = 60 }).Normalized();
        Assert.Equal(80, replacement.LearnedBatteryChargeTargetPercent);
        Assert.Equal(60, replacement.BatteryChargeTargetCandidatePercent);
    }

    [Theory]
    [InlineData(50, 50)]
    [InlineData(100, 100)]
    [InlineData(101, null)]
    public void manual_battery_target_range_is_enforced(int input, int? expected) =>
        Assert.Equal(expected, new AppSettings { BatteryChargeTargetPercent = input }.Normalized().BatteryChargeTargetPercent);
}
