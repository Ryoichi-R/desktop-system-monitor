using DesktopSystemMonitor.Core.Settings;

namespace DesktopSystemMonitor.App;

/// <summary>
/// Applies only values that the settings dialog owns to the latest persisted
/// settings. Runtime changes made while the dialog was open are preserved.
/// </summary>
internal static class SettingsEditMerge
{
    internal static AppSettings Merge(
        AppSettings current,
        SettingsDraft edited,
        bool resetBatteryChargeLearning = false)
    {
        AppSettings merged = (current with
        {
            SamplingIntervalSeconds = edited.SamplingIntervalSeconds,
            UiScalePercent = edited.UiScalePercent,
            BackgroundEnabled = edited.BackgroundEnabled,
            BackgroundOpacity = edited.BackgroundOpacity,
            BackgroundFillMode = edited.BackgroundFillMode,
            BackgroundEdgeFadePercent = edited.BackgroundEdgeFadePercent,
            HideBackgroundBehindWindows = edited.HideBackgroundBehindWindows,
            LayerMode = edited.LayerMode,
            NetworkUnitSystem = edited.NetworkUnitSystem,
            ClickThrough = edited.ClickThrough,
            AutoHideOnFullScreen = edited.AutoHideOnFullScreen,
            StartWithWindows = edited.StartWithWindows,
            DiagnosticLoggingEnabled = edited.DiagnosticLoggingEnabled,
            ShowCpuMetrics = edited.ShowCpuMetrics,
            ShowGpuMetrics = edited.ShowGpuMetrics,
            ShowDiskMetrics = edited.ShowDiskMetrics,
            ShowCpuTemperature = edited.ShowCpuTemperature,
            ShowGpuTemperature = edited.ShowGpuTemperature,
            ShowTemperatures = edited.ShowCpuTemperature || edited.ShowGpuTemperature,
            ShowBatteryEstimate = edited.ShowBatteryEstimate,
            BatteryChargeTargetPercent = edited.BatteryChargeTargetPercent,
            ShowRecentPeaks = edited.ShowRecentPeaks,
            ShowNetworkPeaks = edited.ShowNetworkPeaks,
            EnableHighLoadProcessDetails = edited.EnableHighLoadProcessDetails,
            SelectedPhysicalDiskNumber = edited.SelectedPhysicalDiskNumber,
            NetworkPeakWindowSeconds = edited.NetworkPeakWindowSeconds,
            FontFamilyName = edited.FontFamilyName,
            ForegroundColor = edited.ForegroundColor,
            MutedColor = edited.MutedColor,
            AccentColor = edited.AccentColor,
            RxAccentColor = edited.RxAccentColor,
            TxAccentColor = edited.TxAccentColor,
            BackgroundColor = edited.BackgroundColor,
            PinnedGpuLuidHex = edited.PinnedGpuLuidHex,
            SelectedNetworkAdapterLuids = edited.SelectedNetworkAdapterLuids,
            SavedMonitorDeviceName = edited.SavedMonitorDeviceName,
            SavedRightEdgeDip = edited.SavedRightEdgeDip,
            SavedTopEdgeDip = edited.SavedTopEdgeDip,
            SavedMonitorDpi = edited.SavedMonitorDpi,
            PlacementMode = edited.PlacementMode,
            PlacementAnchor = edited.PlacementAnchor,
            HorizontalMarginDip = edited.HorizontalMarginDip,
            VerticalMarginDip = edited.VerticalMarginDip,
        }).Normalized();
        return resetBatteryChargeLearning
            ? merged with
            {
                LearnedBatteryChargeTargetPercent = null,
                BatteryChargeTargetCandidatePercent = null,
            }
            : merged;
    }
}
