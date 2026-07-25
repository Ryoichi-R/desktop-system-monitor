using System.Collections.Immutable;
using System.Globalization;
using System.Windows;
using System.Windows.Interop;
using DesktopSystemMonitor.Core.Formatting;
using DesktopSystemMonitor.Core.Settings;
using ComboBox = System.Windows.Controls.ComboBox;
using ComboBoxItem = System.Windows.Controls.ComboBoxItem;
using Forms = System.Windows.Forms;

namespace DesktopSystemMonitor.App;

public partial class SettingsWindow : Window
{
    private readonly Func<AppSettings> _captureCurrentPosition;
    private AppSettings _placementSettings;
    private bool _initializingPlacement = true;

    public SettingsWindow(AppSettings settings) : this(settings, SettingsDialogContext.Unknown, () => settings)
    {
    }

    internal SettingsWindow(AppSettings settings, SettingsDialogContext context)
        : this(settings, context, () => settings)
    {
    }

    internal SettingsWindow(
        AppSettings settings,
        SettingsDialogContext context,
        Func<AppSettings> captureCurrentPosition)
    {
        InitializeComponent();
        _placementSettings = settings;
        _captureCurrentPosition = captureCurrentPosition;
        IntervalBox.Text = settings.SamplingIntervalSeconds.ToString(CultureInfo.InvariantCulture);
        ScaleBox.Text = settings.UiScalePercent.ToString(CultureInfo.InvariantCulture);
        InitializePlacementControls(settings);
        SelectByTag(LayerBox, settings.LayerMode.ToString());
        SelectByTag(UnitBox, settings.NetworkUnitSystem.ToString());
        ClickThroughBox.IsChecked = settings.ClickThrough;
        AutoHideBox.IsChecked = settings.AutoHideOnFullScreen;
        StartupBox.IsChecked = settings.StartWithWindows;
        DiagnosticBox.IsChecked = settings.DiagnosticLoggingEnabled;
        ShowCpuBox.IsChecked = settings.ShowCpuMetrics;
        ShowGpuBox.IsChecked = settings.ShowGpuMetrics;
        ShowDiskBox.IsChecked = settings.ShowDiskMetrics;
        ShowCpuTemperatureBox.IsChecked = settings.EffectiveShowCpuTemperature;
        ShowGpuTemperatureBox.IsChecked = settings.EffectiveShowGpuTemperature;
        ShowBatteryBox.IsChecked = settings.ShowBatteryEstimate;
        SelectByTag(BatteryChargeModeBox, settings.BatteryChargeTargetPercent is null ? "Auto" : "Manual");
        BatteryChargeTargetBox.Text = (settings.BatteryChargeTargetPercent ?? 80).ToString(CultureInfo.InvariantCulture);
        BatteryChargeLearningStatusText.Text = DescribeBatteryChargeLearning(settings);
        ResetBatteryChargeLearningButton.IsEnabled =
            settings.LearnedBatteryChargeTargetPercent is not null ||
            settings.BatteryChargeTargetCandidatePercent is not null;
        CpuTemperatureCapabilityText.Text = $"現在の検出状態: {OptionalTelemetryCapabilities.Display(context.Capabilities.CpuTemperature)}";
        GpuTemperatureCapabilityText.Text = $"現在の検出状態: {OptionalTelemetryCapabilities.Display(context.Capabilities.GpuTemperature)}";
        BatteryCapabilityText.Text = $"現在の検出状態: {OptionalTelemetryCapabilities.Display(context.Capabilities.Battery)}";
        ShowPeaksBox.IsChecked = settings.ShowRecentPeaks;
        ShowNetworkPeaksBox.IsChecked = settings.EffectiveShowNetworkPeaks;
        ProcessDetailsBox.IsChecked = settings.EnableHighLoadProcessDetails;
        NetworkPeakWindowBox.Text = settings.NetworkPeakWindowSeconds.ToString(CultureInfo.InvariantCulture);
        var disks = new List<DiskChoice> { new(null, "自動（Windowsがあるディスク）") };
        disks.AddRange(context.AvailableDiskNumbers.Select(number => new DiskChoice(number, $"Disk {number}")));
        PhysicalDiskBox.ItemsSource = disks;
        PhysicalDiskBox.SelectedItem = disks.FirstOrDefault(choice => choice.Number == settings.SelectedPhysicalDiskNumber) ?? disks[0];
        PhysicalDiskBox.IsEnabled = settings.ShowDiskMetrics;
        FontBox.Text = settings.FontFamilyName;
        ForegroundBox.Text = settings.ForegroundColor;
        MutedBox.Text = settings.MutedColor;
        AccentBox.Text = settings.AccentColor;
        RxAccentBox.Text = settings.RxAccentColor;
        TxAccentBox.Text = settings.TxAccentColor;
        SelectByTag(BackgroundFillModeBox, settings.BackgroundFillMode.ToString());
        BackgroundEdgeFadeSlider.Value = settings.BackgroundEdgeFadePercent;
        BackgroundEnabledBox.IsChecked = settings.BackgroundEnabled;
        BackgroundColorBox.Text = settings.EffectiveBackgroundRgb;
        BackgroundOpacitySlider.Value = settings.EffectiveBackgroundOpacity * 100d;
        HideBackgroundBehindWindowsBox.IsChecked = settings.HideBackgroundBehindWindows;
        GpuLuidBox.Text = settings.PinnedGpuLuidHex ?? string.Empty;
        NetworkLuidsBox.Text = string.Join(", ", settings.SelectedNetworkAdapterLuids.Select(v => $"0x{v:X}"));
        DiskStatusText.Text = context.DiskStatusMessage;
        _initializingPlacement = false;
        UpdatePlacementStatus();
        UpdateDependentControls();
    }

    internal SettingsDraft? Result { get; private set; }
    internal bool ResetBatteryChargeLearningRequested { get; private set; }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (!double.TryParse(IntervalBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double interval) || interval is < 0.5 or > 5 ||
            !double.TryParse(ScaleBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double scale) || scale is < 75 or > 200)
        {
            ShowValidationError(0, "全体設定: 数値の範囲を確認してください。");
            return;
        }
        if (!double.TryParse(HorizontalMarginBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double horizontalMargin) || horizontalMargin is < 0 or > 200 ||
            !double.TryParse(VerticalMarginBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double verticalMargin) || verticalMargin is < 0 or > 200)
        {
            ShowValidationError(1, "表示位置: 余白は0～200の数値で入力してください。");
            return;
        }
        if (!int.TryParse(NetworkPeakWindowBox.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int networkPeakWindow) || networkPeakWindow is < 10 or > 60)
        {
            ShowValidationError(6, "NET: MAX集計期間は10～60の整数で入力してください。");
            return;
        }
        if (!TryParseLuids(NetworkLuidsBox.Text, out ImmutableArray<ulong> networkLuids))
        {
            ShowValidationError(6, "NET: Network LUIDは10進または0x付き16進で入力してください。");
            return;
        }
        if (!TryValidateColors(out string colorError))
        {
            ShowValidationError(2, $"外観: {colorError}");
            return;
        }
        int? batteryChargeTarget = null;
        if (string.Equals(SelectedTag(BatteryChargeModeBox), "Manual", StringComparison.Ordinal))
        {
            if (!int.TryParse(BatteryChargeTargetBox.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsedTarget) ||
                parsedTarget is < 50 or > 100)
            {
                ShowValidationError(7, "BAT: 手動目標は50～100の整数で入力してください。");
                return;
            }
            batteryChargeTarget = parsedTarget;
        }
        string? gpuLuid = string.IsNullOrWhiteSpace(GpuLuidBox.Text) ? null : GpuLuidBox.Text.Trim();
        ReadOnlySpan<char> gpuToken = gpuLuid.AsSpan();
        if (gpuToken.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            gpuToken = gpuToken[2..];
        }
        if (gpuLuid is not null && !ulong.TryParse(gpuToken, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out _))
        {
            ShowValidationError(4, "GPU: GPU LUIDは16進数で入力してください。");
            return;
        }

        Result = new SettingsDraft
        {
            SamplingIntervalSeconds = interval,
            UiScalePercent = scale,
            BackgroundEnabled = BackgroundEnabledBox.IsChecked == true,
            BackgroundOpacity = BackgroundOpacitySlider.Value / 100d,
            BackgroundFillMode = Enum.Parse<BackgroundFillMode>(SelectedTag(BackgroundFillModeBox)),
            BackgroundEdgeFadePercent = BackgroundEdgeFadeSlider.Value,
            HideBackgroundBehindWindows = HideBackgroundBehindWindowsBox.IsChecked == true,
            LayerMode = Enum.Parse<WindowLayerMode>(SelectedTag(LayerBox)),
            NetworkUnitSystem = Enum.Parse<RateUnitSystem>(SelectedTag(UnitBox)),
            ClickThrough = ClickThroughBox.IsChecked == true,
            AutoHideOnFullScreen = AutoHideBox.IsChecked == true,
            StartWithWindows = StartupBox.IsChecked == true,
            DiagnosticLoggingEnabled = DiagnosticBox.IsChecked == true,
            ShowCpuMetrics = ShowCpuBox.IsChecked == true,
            ShowGpuMetrics = ShowGpuBox.IsChecked == true,
            ShowDiskMetrics = ShowDiskBox.IsChecked == true,
            ShowCpuTemperature = ShowCpuTemperatureBox.IsChecked == true,
            ShowGpuTemperature = ShowGpuTemperatureBox.IsChecked == true,
            ShowBatteryEstimate = ShowBatteryBox.IsChecked == true,
            BatteryChargeTargetPercent = batteryChargeTarget,
            ShowRecentPeaks = ShowPeaksBox.IsChecked == true,
            ShowNetworkPeaks = ShowNetworkPeaksBox.IsChecked == true,
            EnableHighLoadProcessDetails = ProcessDetailsBox.IsChecked == true,
            SelectedPhysicalDiskNumber = (PhysicalDiskBox.SelectedItem as DiskChoice)?.Number,
            NetworkPeakWindowSeconds = networkPeakWindow,
            FontFamilyName = FontBox.Text,
            ForegroundColor = ForegroundBox.Text,
            MutedColor = MutedBox.Text,
            AccentColor = AccentBox.Text,
            RxAccentColor = RxAccentBox.Text,
            TxAccentColor = TxAccentBox.Text,
            BackgroundColor = $"#FF{BackgroundColorBox.Text.Trim()[1..].ToUpperInvariant()}",
            PinnedGpuLuidHex = gpuLuid,
            SelectedNetworkAdapterLuids = networkLuids,
            SavedMonitorDeviceName = _placementSettings.PlacementMode == WindowPlacementMode.Custom
                ? _placementSettings.SavedMonitorDeviceName
                : (MonitorBox.SelectedItem as MonitorChoice)?.DeviceName,
            SavedRightEdgeDip = _placementSettings.PlacementMode == WindowPlacementMode.Custom
                ? _placementSettings.SavedRightEdgeDip
                : null,
            SavedTopEdgeDip = _placementSettings.PlacementMode == WindowPlacementMode.Custom
                ? _placementSettings.SavedTopEdgeDip
                : null,
            SavedMonitorDpi = _placementSettings.PlacementMode == WindowPlacementMode.Custom
                ? _placementSettings.SavedMonitorDpi
                : null,
            PlacementMode = _placementSettings.PlacementMode,
            PlacementAnchor = (PlacementAnchorBox.SelectedItem as PlacementAnchorChoice)?.Value
                ?? WindowPlacementAnchor.TopRight,
            HorizontalMarginDip = horizontalMargin,
            VerticalMarginDip = verticalMargin,
        };
        DialogResult = true;
    }

    private void CpuToggle_Changed(object sender, RoutedEventArgs e) => UpdateDependentControls();
    private void GpuToggle_Changed(object sender, RoutedEventArgs e) => UpdateDependentControls();
    private void NetworkPeakToggle_Changed(object sender, RoutedEventArgs e) => UpdateDependentControls();
    private void BatteryChargeMode_Changed(object sender, System.Windows.Controls.SelectionChangedEventArgs e) => UpdateDependentControls();
    private void Layer_Changed(object sender, System.Windows.Controls.SelectionChangedEventArgs e) => UpdateDependentControls();
    private void BackgroundSetting_Changed(object sender, RoutedEventArgs e) => UpdateDependentControls();

    private void PlacementPreset_Changed(object sender, RoutedEventArgs e)
    {
        if (_initializingPlacement)
        {
            return;
        }
        _placementSettings = _placementSettings with { PlacementMode = WindowPlacementMode.Preset };
        UpdatePlacementStatus();
    }

    private void UseCurrentPosition_Click(object sender, RoutedEventArgs e)
    {
        _placementSettings = _captureCurrentPosition().Normalized() with
        {
            PlacementMode = WindowPlacementMode.Custom,
        };
        _initializingPlacement = true;
        SelectMonitor(_placementSettings.SavedMonitorDeviceName);
        _initializingPlacement = false;
        UpdatePlacementStatus();
    }

    private void ResetPosition_Click(object sender, RoutedEventArgs e)
    {
        _placementSettings = _placementSettings with
        {
            PlacementMode = WindowPlacementMode.Preset,
            PlacementAnchor = WindowPlacementAnchor.TopRight,
            HorizontalMarginDip = 8,
            VerticalMarginDip = 8,
        };
        _initializingPlacement = true;
        MonitorBox.SelectedIndex = 0;
        PlacementAnchorBox.SelectedItem = PlacementAnchorBox.Items.Cast<PlacementAnchorChoice>()
            .First(choice => choice.Value == WindowPlacementAnchor.TopRight);
        HorizontalMarginBox.Text = "8";
        VerticalMarginBox.Text = "8";
        _initializingPlacement = false;
        UpdatePlacementStatus();
    }

    private void BackgroundOpacity_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (BackgroundOpacityText is not null)
        {
            BackgroundOpacityText.Text = $"{Math.Round(e.NewValue):0}%";
        }
        UpdateBackgroundPreview();
    }

    private void BackgroundColor_Changed(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        if (BackgroundColorPreview is null)
        {
            return;
        }
        UpdateBackgroundPreview();
    }

    private void BackgroundGradientSetting_Changed(object sender, RoutedEventArgs e)
    {
        if (BackgroundEdgeFadeText is not null && BackgroundEdgeFadeSlider is not null)
        {
            BackgroundEdgeFadeText.Text = $"{Math.Round(BackgroundEdgeFadeSlider.Value):0}%";
        }
        UpdateDependentControls();
        UpdateBackgroundPreview();
    }

    private void UpdateBackgroundPreview()
    {
        if (BackgroundColorPreview is null ||
            BackgroundColorBox is null ||
            BackgroundOpacitySlider is null ||
            BackgroundFillModeBox?.SelectedItem is null ||
            BackgroundEdgeFadeSlider is null ||
            !IsValidRgbColor(BackgroundColorBox.Text))
        {
            if (BackgroundColorPreview is not null)
            {
                BackgroundBrushFactory.Clear(BackgroundColorPreviewFadeHost, BackgroundColorPreview);
            }
            return;
        }

        BackgroundBrushFactory.Apply(BackgroundColorPreviewFadeHost, BackgroundColorPreview, new AppSettings
        {
            BackgroundColor = $"#FF{BackgroundColorBox.Text.Trim()[1..].ToUpperInvariant()}",
            BackgroundOpacity = BackgroundOpacitySlider.Value / 100d,
            BackgroundFillMode = Enum.Parse<BackgroundFillMode>(SelectedTag(BackgroundFillModeBox)),
            BackgroundEdgeFadePercent = BackgroundEdgeFadeSlider.Value,
        }.Normalized());
    }

    private void ChooseBackgroundColor_Click(object sender, RoutedEventArgs e)
    {
        using var dialog = new Forms.ColorDialog { FullOpen = true };
        if (IsValidRgbColor(BackgroundColorBox.Text))
        {
            dialog.Color = System.Drawing.ColorTranslator.FromHtml(BackgroundColorBox.Text);
        }
        var owner = new Win32DialogOwner(new WindowInteropHelper(this).Handle);
        if (dialog.ShowDialog(owner) == Forms.DialogResult.OK)
        {
            BackgroundColorBox.Text = $"#{dialog.Color.R:X2}{dialog.Color.G:X2}{dialog.Color.B:X2}";
        }
    }

    private void ResetBatteryChargeLearning_Click(object sender, RoutedEventArgs e)
    {
        ResetBatteryChargeLearningRequested = true;
        ResetBatteryChargeLearningButton.IsEnabled = false;
        BatteryChargeLearningStatusText.Text = "保存時に学習値と候補をリセットします。";
    }

    private void ShowValidationError(int categoryIndex, string message)
    {
        CategoryTabs.SelectedIndex = categoryIndex;
        ValidationMessage.Text = message;
    }

    private void UpdateDependentControls()
    {
        if (ShowCpuTemperatureBox is not null) ShowCpuTemperatureBox.IsEnabled = ShowCpuBox.IsChecked == true;
        if (ShowGpuTemperatureBox is not null) ShowGpuTemperatureBox.IsEnabled = ShowGpuBox.IsChecked == true;
        if (NetworkPeakWindowBox is not null) NetworkPeakWindowBox.IsEnabled = ShowNetworkPeaksBox.IsChecked == true;
        if (BatteryChargeTargetBox is not null && BatteryChargeModeBox?.SelectedItem is not null)
        {
            BatteryChargeTargetBox.IsEnabled = string.Equals(
                SelectedTag(BatteryChargeModeBox),
                "Manual",
                StringComparison.Ordinal);
        }
        if (BackgroundEnabledBox is not null &&
            LayerBox?.SelectedItem is not null &&
            BackgroundFillModeBox?.SelectedItem is not null)
        {
            bool backgroundEnabled = BackgroundEnabledBox.IsChecked == true;
            bool alwaysOnTop = string.Equals(SelectedTag(LayerBox), nameof(WindowLayerMode.AlwaysOnTop), StringComparison.Ordinal);
            BackgroundColorBox.IsEnabled = backgroundEnabled;
            ChooseBackgroundColorButton.IsEnabled = backgroundEnabled;
            BackgroundOpacitySlider.IsEnabled = backgroundEnabled;
            BackgroundFillModeBox.IsEnabled = backgroundEnabled;
            bool edgeFade = backgroundEnabled &&
                string.Equals(SelectedTag(BackgroundFillModeBox), nameof(BackgroundFillMode.EdgeFade), StringComparison.Ordinal);
            BackgroundEdgeFadeSlider.IsEnabled = edgeFade;
            HideBackgroundBehindWindowsBox.IsEnabled = backgroundEnabled && alwaysOnTop;
            BackgroundLayerHint.Text = alwaysOnTop
                ? "有効にすると、通常ウィンドウと重なる部分の背景だけが隠れ、情報は手前に残ります。"
                : "背景だけを隠す機能は「常に手前」の場合のみ適用されます。設定値は保持されます。";
        }
    }

    private void DiskToggle_Changed(object sender, RoutedEventArgs e)
    {
        if (PhysicalDiskBox is not null)
        {
            PhysicalDiskBox.IsEnabled = ShowDiskBox.IsChecked == true;
        }
    }

    private bool TryValidateColors(out string error)
    {
        (string Name, string Value)[] colors =
        [
            ("文字色", ForegroundBox.Text),
            ("補助文字色", MutedBox.Text),
            ("アクセント色", AccentBox.Text),
            ("受信色", RxAccentBox.Text),
            ("送信色", TxAccentBox.Text),
        ];
        foreach ((string name, string value) in colors)
        {
            if (!AppSettings.IsValidColor(value))
            {
                error = $"{name}は #RRGGBB または #AARRGGBB 形式で入力してください。";
                return false;
            }
        }
        if (!IsValidRgbColor(BackgroundColorBox.Text))
        {
            error = "背景色は #RRGGBB 形式で入力してください。";
            return false;
        }
        error = string.Empty;
        return true;
    }

    private static bool IsValidRgbColor(string? value)
    {
        if (value is not { Length: 7 } || value[0] != '#')
        {
            return false;
        }
        for (int index = 1; index < value.Length; index++)
        {
            if (!Uri.IsHexDigit(value[index]))
            {
                return false;
            }
        }
        return true;
    }

    private static bool TryParseLuids(string text, out ImmutableArray<ulong> values)
    {
        var builder = ImmutableArray.CreateBuilder<ulong>();
        foreach (string raw in text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            ReadOnlySpan<char> token = raw.AsSpan();
            NumberStyles style = NumberStyles.Integer;
            if (token.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            {
                token = token[2..];
                style = NumberStyles.HexNumber;
            }
            if (!ulong.TryParse(token, style, CultureInfo.InvariantCulture, out ulong luid))
            {
                values = ImmutableArray<ulong>.Empty;
                return false;
            }
            builder.Add(luid);
        }
        values = builder.Distinct().ToImmutableArray();
        return true;
    }

    private void InitializePlacementControls(AppSettings settings)
    {
        var monitors = new List<MonitorChoice>
        {
            new(null, "自動（プライマリ）"),
        };
        monitors.AddRange(Forms.Screen.AllScreens.Select(screen =>
            new MonitorChoice(
                screen.DeviceName,
                screen.DeviceName + (screen.Primary ? "（プライマリ）" : string.Empty))));
        if (settings.SavedMonitorDeviceName is { Length: > 0 } savedDevice &&
            monitors.All(choice => !string.Equals(choice.DeviceName, savedDevice, StringComparison.OrdinalIgnoreCase)))
        {
            monitors.Add(new MonitorChoice(savedDevice, $"{savedDevice}（現在未接続）"));
        }
        MonitorBox.ItemsSource = monitors;
        SelectMonitor(settings.SavedMonitorDeviceName);

        PlacementAnchorBox.ItemsSource = new[]
        {
            new PlacementAnchorChoice(WindowPlacementAnchor.TopRight, "右上"),
            new PlacementAnchorChoice(WindowPlacementAnchor.BottomRight, "右下"),
            new PlacementAnchorChoice(WindowPlacementAnchor.TopLeft, "左上"),
            new PlacementAnchorChoice(WindowPlacementAnchor.BottomLeft, "左下"),
        };
        PlacementAnchorBox.SelectedItem = PlacementAnchorBox.Items.Cast<PlacementAnchorChoice>()
            .First(choice => choice.Value == settings.PlacementAnchor);
        HorizontalMarginBox.Text = settings.HorizontalMarginDip.ToString(CultureInfo.InvariantCulture);
        VerticalMarginBox.Text = settings.VerticalMarginDip.ToString(CultureInfo.InvariantCulture);
    }

    private void SelectMonitor(string? deviceName)
    {
        MonitorBox.SelectedItem = MonitorBox.Items.Cast<MonitorChoice>().FirstOrDefault(choice =>
            string.Equals(choice.DeviceName, deviceName, StringComparison.OrdinalIgnoreCase))
            ?? MonitorBox.Items[0];
    }

    private void UpdatePlacementStatus()
    {
        PlacementStatusText.Text = _placementSettings.PlacementMode == WindowPlacementMode.Custom
            ? "保存時は、メイン表示の現在位置を使用します。"
            : "保存時は、選択したモニター・基準位置・余白から表示位置を決定します。";
    }

    private static void SelectByTag(ComboBox box, string tag)
    {
        box.SelectedItem = box.Items.Cast<ComboBoxItem>().First(item => string.Equals(item.Tag?.ToString(), tag, StringComparison.Ordinal));
    }

    private static string SelectedTag(ComboBox box) => ((ComboBoxItem)box.SelectedItem).Tag!.ToString()!;

    private static string DescribeBatteryChargeLearning(AppSettings settings)
    {
        if (settings.BatteryChargeTargetPercent is { } manual)
        {
            return $"手動目標 {manual}%（自動学習値より優先）";
        }
        if (settings.LearnedBatteryChargeTargetPercent is { } learned)
        {
            return settings.BatteryChargeTargetCandidatePercent is { } replacement
                ? $"学習値 {learned}%／新しい候補 {replacement}%（1/2）"
                : $"学習値 {learned}%";
        }
        return settings.BatteryChargeTargetCandidatePercent is { } candidate
            ? $"候補 {candidate}%（1/2）"
            : "未学習（100%として推定）";
    }

    private sealed record DiskChoice(int? Number, string Label);

    private sealed record MonitorChoice(string? DeviceName, string Label);

    private sealed record PlacementAnchorChoice(WindowPlacementAnchor Value, string Label);

    private sealed class Win32DialogOwner(IntPtr handle) : Forms.IWin32Window
    {
        public IntPtr Handle { get; } = handle;
    }
}
