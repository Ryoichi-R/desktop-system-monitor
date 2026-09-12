using System.Collections.Immutable;
using DesktopSystemMonitor.Core.Formatting;

namespace DesktopSystemMonitor.Core.Settings;

public enum WindowLayerMode
{
    /// <summary>Non-topmost widget that stays visually on the desktop plane.</summary>
    OnDesktop,
    /// <summary>Always-on-top with optional fullscreen auto-hide.</summary>
    AlwaysOnTop,
    /// <summary>Normal non-topmost Z-order, including safe fallback.</summary>
    Normal,
}

public enum BatteryChargeTargetSource
{
    Fallback,
    Learned,
    Manual,
}

public enum BackgroundFillMode
{
    Solid,
    EdgeFade,
}

public enum WidgetDisplayMode
{
    Standard,
    Reduced,
}

public enum WindowPlacementMode
{
    Preset,
    Custom,
}

public enum WindowPlacementAnchor
{
    TopRight,
    BottomRight,
    TopLeft,
    BottomLeft,
}

public sealed record AppSettings
{
    public const int CurrentSchemaVersion = 5;

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;

    public double SamplingIntervalSeconds { get; init; } = 1.0;
    public double UiScalePercent { get; init; } = 100.0;
    public WidgetDisplayMode DisplayMode { get; init; } = WidgetDisplayMode.Standard;
    public double Opacity { get; init; } = 1.0;
    public bool BackgroundEnabled { get; init; }
    public double? BackgroundOpacity { get; init; }
    public BackgroundFillMode BackgroundFillMode { get; init; }
    public double BackgroundEdgeFadePercent { get; init; } = 22;
    public bool HideBackgroundBehindWindows { get; init; }
    public WindowLayerMode LayerMode { get; init; } = WindowLayerMode.AlwaysOnTop;
    public bool ClickThrough { get; init; } = true;
    public bool AutoHideOnFullScreen { get; init; } = true;
    public bool StartWithWindows { get; init; }
    public bool DiagnosticLoggingEnabled { get; init; }
    public bool ShowCpuMetrics { get; init; } = true;
    public bool ShowGpuMetrics { get; init; } = true;
    public bool ShowDiskMetrics { get; init; }
    public bool ShowTemperatures { get; init; }
    public bool? ShowCpuTemperature { get; init; }
    public bool? ShowGpuTemperature { get; init; }
    public bool ShowBatteryEstimate { get; init; }
    public int? BatteryChargeTargetPercent { get; init; }
    public int? LearnedBatteryChargeTargetPercent { get; init; }
    public int? BatteryChargeTargetCandidatePercent { get; init; }
    public bool ShowRecentPeaks { get; init; }
    public bool? ShowNetworkPeaks { get; init; }
    public bool EnableHighLoadProcessDetails { get; init; }
    public int? SelectedPhysicalDiskNumber { get; init; }
    public RateUnitSystem NetworkUnitSystem { get; init; } = RateUnitSystem.DecimalBytes;
    public int NetworkPeakWindowSeconds { get; init; } = 60;
    public bool EffectiveShowCpuTemperature => ShowCpuTemperature ?? ShowTemperatures;
    public bool EffectiveShowGpuTemperature => ShowGpuTemperature ?? ShowTemperatures;
    public bool EffectiveShowNetworkPeaks => ShowNetworkPeaks ?? ShowRecentPeaks;
    public int EffectiveBatteryChargeTargetPercent =>
        BatteryChargeTargetPercent ?? LearnedBatteryChargeTargetPercent ?? 100;
    public BatteryChargeTargetSource EffectiveBatteryChargeTargetSource =>
        BatteryChargeTargetPercent is not null
            ? BatteryChargeTargetSource.Manual
            : LearnedBatteryChargeTargetPercent is not null
                ? BatteryChargeTargetSource.Learned
                : BatteryChargeTargetSource.Fallback;
    public string FontFamilyName { get; init; } = "Segoe UI Variable Text";
    public string ForegroundColor { get; init; } = "#FFF3F3F3";
    public string MutedColor { get; init; } = "#FFB0B0B0";
    public string AccentColor { get; init; } = "#FF7DC8FF";
    public string RxAccentColor { get; init; } = "#FF5DCAA5";
    public string TxAccentColor { get; init; } = "#FFEF9F27";
    public string BackgroundColor { get; init; } = "#B0000000";
    public double EffectiveBackgroundOpacity => BackgroundOpacity ?? BackgroundAlpha(BackgroundColor);
    public string EffectiveBackgroundRgb => $"#{NormalizeColor(BackgroundColor, "#B0000000")[^6..]}";

    public string? PinnedGpuLuidHex { get; init; }
    public ImmutableArray<ulong> SelectedNetworkAdapterLuids { get; init; } = ImmutableArray<ulong>.Empty;

    public string? SavedMonitorDeviceName { get; init; }
    public double? SavedRightEdgeDip { get; init; }
    public double? SavedTopEdgeDip { get; init; }
    public double? SavedMonitorDpi { get; init; }
    public double? PlacementXRatio { get; init; }
    public double? PlacementYRatio { get; init; }
    public double? SavedWorkAreaWidthDip { get; init; }
    public double? SavedWorkAreaHeightDip { get; init; }
    public WindowPlacementMode PlacementMode { get; init; } = WindowPlacementMode.Preset;
    public WindowPlacementAnchor PlacementAnchor { get; init; } = WindowPlacementAnchor.TopRight;
    public double HorizontalMarginDip { get; init; } = 8;
    public double VerticalMarginDip { get; init; } = 8;

    public AppSettings Normalized()
    {
        double interval = SamplingIntervalSeconds;
        if (double.IsNaN(interval) || double.IsInfinity(interval))
        {
            interval = 1.0;
        }
        interval = Math.Clamp(interval, 0.5, 5.0);

        double scale = double.IsFinite(UiScalePercent) ? Math.Clamp(UiScalePercent, 75, 200) : 100;
        WidgetDisplayMode displayMode = Enum.IsDefined(DisplayMode)
            ? DisplayMode
            : WidgetDisplayMode.Standard;
        double opacity = double.IsFinite(Opacity) ? Math.Clamp(Opacity, 0.2, 1.0) : 1.0;
        double? backgroundOpacity = BackgroundOpacity is { } candidateOpacity
            ? double.IsFinite(candidateOpacity) ? Math.Clamp(candidateOpacity, 0, 1) : null
            : null;
        BackgroundFillMode backgroundFillMode = Enum.IsDefined(BackgroundFillMode)
            ? BackgroundFillMode
            : global::DesktopSystemMonitor.Core.Settings.BackgroundFillMode.Solid;
        double backgroundEdgeFadePercent = double.IsFinite(BackgroundEdgeFadePercent)
            ? Math.Clamp(BackgroundEdgeFadePercent, 5, 50)
            : 22;
        string font = string.IsNullOrWhiteSpace(FontFamilyName) ? "Segoe UI Variable Text" : FontFamilyName.Trim();
        WindowLayerMode layerMode = Enum.IsDefined(LayerMode) ? LayerMode : WindowLayerMode.AlwaysOnTop;
        RateUnitSystem networkUnitSystem = Enum.IsDefined(NetworkUnitSystem)
            ? NetworkUnitSystem
            : RateUnitSystem.DecimalBytes;
        WindowPlacementMode placementMode = Enum.IsDefined(PlacementMode)
            ? PlacementMode
            : WindowPlacementMode.Preset;
        WindowPlacementAnchor placementAnchor = Enum.IsDefined(PlacementAnchor)
            ? PlacementAnchor
            : WindowPlacementAnchor.TopRight;
        double horizontalMargin = double.IsFinite(HorizontalMarginDip)
            ? Math.Clamp(HorizontalMarginDip, 0, 200)
            : 8;
        double verticalMargin = double.IsFinite(VerticalMarginDip)
            ? Math.Clamp(VerticalMarginDip, 0, 200)
            : 8;
        double? placementXRatio = PlacementXRatio is { } xRatio
            && double.IsFinite(xRatio)
            && xRatio is >= 0 and <= 1
            ? xRatio
            : null;
        double? placementYRatio = PlacementYRatio is { } yRatio
            && double.IsFinite(yRatio)
            && yRatio is >= 0 and <= 1
            ? yRatio
            : null;
        double? savedWorkAreaWidth = SavedWorkAreaWidthDip is { } width
            && double.IsFinite(width)
            && width > 0
            ? width
            : null;
        double? savedWorkAreaHeight = SavedWorkAreaHeightDip is { } height
            && double.IsFinite(height)
            && height > 0
            ? height
            : null;
        if (placementXRatio is null
            || placementYRatio is null
            || savedWorkAreaWidth is null
            || savedWorkAreaHeight is null)
        {
            placementXRatio = null;
            placementYRatio = null;
            savedWorkAreaWidth = null;
            savedWorkAreaHeight = null;
        }
        int? manualBatteryTarget = BatteryChargeTargetPercent is >= 50 and <= 100
            ? BatteryChargeTargetPercent
            : null;
        int? learnedBatteryTarget = LearnedBatteryChargeTargetPercent is >= 50 and <= 99
            ? LearnedBatteryChargeTargetPercent
            : null;
        int? batteryTargetCandidate = BatteryChargeTargetCandidatePercent is >= 50 and <= 99
            ? BatteryChargeTargetCandidatePercent
            : null;
        if (learnedBatteryTarget is { } learned &&
            batteryTargetCandidate is { } candidate &&
            Math.Abs(learned - candidate) <= 1)
        {
            batteryTargetCandidate = null;
        }

        return this with
        {
            SchemaVersion = CurrentSchemaVersion,
            SamplingIntervalSeconds = interval,
            UiScalePercent = scale,
            DisplayMode = displayMode,
            Opacity = opacity,
            BackgroundOpacity = backgroundOpacity,
            BackgroundFillMode = backgroundFillMode,
            BackgroundEdgeFadePercent = backgroundEdgeFadePercent,
            LayerMode = layerMode,
            NetworkUnitSystem = networkUnitSystem,
            PlacementMode = placementMode,
            PlacementAnchor = placementAnchor,
            HorizontalMarginDip = horizontalMargin,
            VerticalMarginDip = verticalMargin,
            PlacementXRatio = placementXRatio,
            PlacementYRatio = placementYRatio,
            SavedWorkAreaWidthDip = savedWorkAreaWidth,
            SavedWorkAreaHeightDip = savedWorkAreaHeight,
            NetworkPeakWindowSeconds = Math.Clamp(NetworkPeakWindowSeconds, 10, 60),
            FontFamilyName = font,
            ForegroundColor = NormalizeColor(ForegroundColor, "#FFF3F3F3"),
            MutedColor = NormalizeColor(MutedColor, "#FFB0B0B0"),
            AccentColor = NormalizeColor(AccentColor, "#FF7DC8FF"),
            RxAccentColor = NormalizeColor(RxAccentColor, "#FF5DCAA5"),
            TxAccentColor = NormalizeColor(TxAccentColor, "#FFEF9F27"),
            BackgroundColor = NormalizeColor(BackgroundColor, "#B0000000"),
            SelectedPhysicalDiskNumber = SelectedPhysicalDiskNumber is >= 0 ? SelectedPhysicalDiskNumber : null,
            BatteryChargeTargetPercent = manualBatteryTarget,
            LearnedBatteryChargeTargetPercent = learnedBatteryTarget,
            BatteryChargeTargetCandidatePercent = batteryTargetCandidate,
        };
    }

    public static bool IsValidColor(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }
        string candidate = value.Trim();
        if ((candidate.Length != 7 && candidate.Length != 9) || candidate[0] != '#')
        {
            return false;
        }
        for (int index = 1; index < candidate.Length; index++)
        {
            if (!Uri.IsHexDigit(candidate[index]))
            {
                return false;
            }
        }
        return true;
    }

    private static string NormalizeColor(string? value, string fallback)
    {
        return IsValidColor(value) ? value!.Trim().ToUpperInvariant() : fallback;
    }

    private static double BackgroundAlpha(string value)
    {
        string normalized = NormalizeColor(value, "#B0000000");
        return normalized.Length == 9
            ? byte.Parse(
                normalized.AsSpan(1, 2),
                System.Globalization.NumberStyles.HexNumber,
                System.Globalization.CultureInfo.InvariantCulture) / 255d
            : 1d;
    }
}
