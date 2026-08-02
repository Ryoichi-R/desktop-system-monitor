using System.Collections.Immutable;
using DesktopSystemMonitor.Core.Formatting;
using DesktopSystemMonitor.Core.Settings;

namespace DesktopSystemMonitor.App;

/// <summary>Typed set of values exclusively owned by the settings dialog.</summary>
internal sealed record SettingsDraft
{
    public required double SamplingIntervalSeconds { get; init; }
    public required double UiScalePercent { get; init; }
    public required WidgetDisplayMode DisplayMode { get; init; }
    public required bool BackgroundEnabled { get; init; }
    public required double BackgroundOpacity { get; init; }
    public required BackgroundFillMode BackgroundFillMode { get; init; }
    public required double BackgroundEdgeFadePercent { get; init; }
    public required bool HideBackgroundBehindWindows { get; init; }
    public required WindowLayerMode LayerMode { get; init; }
    public required RateUnitSystem NetworkUnitSystem { get; init; }
    public required bool ClickThrough { get; init; }
    public required bool AutoHideOnFullScreen { get; init; }
    public required bool StartWithWindows { get; init; }
    public required bool DiagnosticLoggingEnabled { get; init; }
    public required bool ShowCpuMetrics { get; init; }
    public required bool ShowGpuMetrics { get; init; }
    public required bool ShowDiskMetrics { get; init; }
    public required bool ShowCpuTemperature { get; init; }
    public required bool ShowGpuTemperature { get; init; }
    public required bool ShowBatteryEstimate { get; init; }
    public required int? BatteryChargeTargetPercent { get; init; }
    public required bool ShowRecentPeaks { get; init; }
    public required bool ShowNetworkPeaks { get; init; }
    public required bool EnableHighLoadProcessDetails { get; init; }
    public required int? SelectedPhysicalDiskNumber { get; init; }
    public required int NetworkPeakWindowSeconds { get; init; }
    public required string FontFamilyName { get; init; }
    public required string ForegroundColor { get; init; }
    public required string MutedColor { get; init; }
    public required string AccentColor { get; init; }
    public required string RxAccentColor { get; init; }
    public required string TxAccentColor { get; init; }
    public required string BackgroundColor { get; init; }
    public required string? PinnedGpuLuidHex { get; init; }
    public required ImmutableArray<ulong> SelectedNetworkAdapterLuids { get; init; }
    public required string? SavedMonitorDeviceName { get; init; }
    public required double? SavedRightEdgeDip { get; init; }
    public required double? SavedTopEdgeDip { get; init; }
    public required double? SavedMonitorDpi { get; init; }
    public required WindowPlacementMode PlacementMode { get; init; }
    public required WindowPlacementAnchor PlacementAnchor { get; init; }
    public required double HorizontalMarginDip { get; init; }
    public required double VerticalMarginDip { get; init; }

    internal static SettingsDraft FromSettings(AppSettings settings) => new()
    {
        SamplingIntervalSeconds = settings.SamplingIntervalSeconds,
        UiScalePercent = settings.UiScalePercent,
        DisplayMode = settings.DisplayMode,
        BackgroundEnabled = settings.BackgroundEnabled,
        BackgroundOpacity = settings.EffectiveBackgroundOpacity,
        BackgroundFillMode = settings.BackgroundFillMode,
        BackgroundEdgeFadePercent = settings.BackgroundEdgeFadePercent,
        HideBackgroundBehindWindows = settings.HideBackgroundBehindWindows,
        LayerMode = settings.LayerMode,
        NetworkUnitSystem = settings.NetworkUnitSystem,
        ClickThrough = settings.ClickThrough,
        AutoHideOnFullScreen = settings.AutoHideOnFullScreen,
        StartWithWindows = settings.StartWithWindows,
        DiagnosticLoggingEnabled = settings.DiagnosticLoggingEnabled,
        ShowCpuMetrics = settings.ShowCpuMetrics,
        ShowGpuMetrics = settings.ShowGpuMetrics,
        ShowDiskMetrics = settings.ShowDiskMetrics,
        ShowCpuTemperature = settings.EffectiveShowCpuTemperature,
        ShowGpuTemperature = settings.EffectiveShowGpuTemperature,
        ShowBatteryEstimate = settings.ShowBatteryEstimate,
        BatteryChargeTargetPercent = settings.BatteryChargeTargetPercent,
        ShowRecentPeaks = settings.ShowRecentPeaks,
        ShowNetworkPeaks = settings.EffectiveShowNetworkPeaks,
        EnableHighLoadProcessDetails = settings.EnableHighLoadProcessDetails,
        SelectedPhysicalDiskNumber = settings.SelectedPhysicalDiskNumber,
        NetworkPeakWindowSeconds = settings.NetworkPeakWindowSeconds,
        FontFamilyName = settings.FontFamilyName,
        ForegroundColor = settings.ForegroundColor,
        MutedColor = settings.MutedColor,
        AccentColor = settings.AccentColor,
        RxAccentColor = settings.RxAccentColor,
        TxAccentColor = settings.TxAccentColor,
        BackgroundColor = $"#FF{settings.EffectiveBackgroundRgb[1..]}",
        PinnedGpuLuidHex = settings.PinnedGpuLuidHex,
        SelectedNetworkAdapterLuids = settings.SelectedNetworkAdapterLuids,
        SavedMonitorDeviceName = settings.SavedMonitorDeviceName,
        SavedRightEdgeDip = settings.SavedRightEdgeDip,
        SavedTopEdgeDip = settings.SavedTopEdgeDip,
        SavedMonitorDpi = settings.SavedMonitorDpi,
        PlacementMode = settings.PlacementMode,
        PlacementAnchor = settings.PlacementAnchor,
        HorizontalMarginDip = settings.HorizontalMarginDip,
        VerticalMarginDip = settings.VerticalMarginDip,
    };
}
