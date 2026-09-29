using Avalonia;
using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Controls.Shapes;
using Avalonia.Interactivity;
using Avalonia.Threading;
using DesktopSystemMonitor.Core.Platform;
using DesktopSystemMonitor.Core.Settings;

using DesktopSystemMonitor.Core.Metrics;
using DesktopSystemMonitor.Core.Formatting;
using DesktopSystemMonitor.Mac;

namespace DesktopSystemMonitor.App;

public sealed partial class AvaloniaMainWindow : Window, IAsyncDisposable
{
    private readonly CpuPeakHistory _cpuPeaks = new();
    private readonly List<(MenuItem Item, int Seconds)> _cpuPeakMenus = new();
    internal int CpuPeakWindowSeconds => _settings.CpuPeakWindowSeconds;
    private readonly NetworkPeakHistory _networkPeaks = new();
    private readonly List<(MenuItem Item, int Seconds)> _peakMenus = new();
    internal int NetworkPeakWindowSeconds => _settings.NetworkPeakWindowSeconds;
    private const double StandardWidgetWidth = 280;
    private const double ReducedWidgetWidth = 150;
    private const double WidgetHeight = 220;
    private IMacWidgetSettingsStore? _settingsStore;
    private MacWidgetSettings _settings = new();
    private readonly DispatcherTimer _saveTimer = new() { Interval = TimeSpan.FromMilliseconds(500) };
    private bool _restoringSettings;
    private bool _positionReady;
    internal event EventHandler? WidgetSettingsChanged;
    internal double WidgetScale => _settings.Scale;
    internal WidgetDisplayMode DisplayMode => _settings.DisplayMode;
    internal bool DesktopLayer => _settings.DesktopLayer;
    internal bool ClickThrough => _settings.ClickThrough;
    internal bool StartAtLogin => _settings.StartAtLogin;
    internal string SettingsMessage { get; private set; } = "設定はこの起動中のみ有効";
    private IMacSensorSession? _session;
    private DispatcherTimer? _timer;
    private readonly CancellationTokenSource _lifetime = new();
    private bool _sampling;
    private bool _disposed;
    private readonly IMetricSource<CpuSnapshot> _cpuSource;
    private readonly IMetricSource<MemorySnapshot> _memorySource;
    private readonly IGpuMetricSource _gpuSource;
    private MacWidgetLayerController? _layerController;
    private IStartupRegistry? _startupRegistry;

    public AvaloniaMainWindow()
        : this(new MacMetricSourceFactory())
    {
    }

    internal AvaloniaMainWindow(IMetricSourceFactory sourceFactory)
    {
        ArgumentNullException.ThrowIfNull(sourceFactory);
        _cpuSource = sourceFactory.CreateCpu();
        _memorySource = sourceFactory.CreateMemory();
        _gpuSource = sourceFactory.CreateGpu();
        InitializeComponent();
        foreach (int seconds in new[] { 10, 20, 30, 60 })
        {
            var item = new MenuItem { Header = $"過去{seconds}秒", ToggleType = MenuItemToggleType.Radio, IsChecked = seconds == CpuPeakWindowSeconds };
            item.Click += (_, _) => SetCpuPeakWindow(seconds);
            CpuPeakWindowMenu.Items.Add(item);
            _cpuPeakMenus.Add((item, seconds));
        }
        foreach (int seconds in new[] { 10, 20, 30, 60 })
        {
            var item = new MenuItem { Header = $"過去{seconds}秒", ToggleType = MenuItemToggleType.Radio, IsChecked = seconds == NetworkPeakWindowSeconds };
            item.Click += (_, _) => SetNetworkPeakWindow(seconds);
            NetworkPeakWindowMenu.Items.Add(item);
            _peakMenus.Add((item, seconds));
        }
        Closed += OnClosed;
        Opened += (_, _) =>
        {
            RestorePosition();
            Screens.Changed += OnScreensChanged;
            AttachMacLayer();
        };
        PositionChanged += (_, _) =>
        {
            if (!_positionReady || _restoringSettings || _disposed) return;
            _settings = _settings with { X = Position.X, Y = Position.Y };
            _saveTimer.Stop();
            _saveTimer.Start();
        };
        _saveTimer.Tick += (_, _) => SaveSettings();
    }

    private async void OnRefreshClick(object? sender, RoutedEventArgs e)
    {
        if (_session is not null) { await RefreshLiveAsync(); return; }
        try
        {
            CpuSnapshot cpu = await _cpuSource.SampleAsync(CancellationToken.None);
            MemorySnapshot memory = await _memorySource.SampleAsync(CancellationToken.None);
            GpuSnapshot gpu = await _gpuSource.SampleAsync(CancellationToken.None);

            UpdateMetric("Cpu", cpu.UtilizationStatus == MetricStatus.Ok, cpu.UtilizationPercent);
            UpdateMetric("Memory", memory.Status == MetricStatus.Ok, memory.UtilizationPercent);
            UpdateMetric("Gpu", gpu.OverallStatus == MetricStatus.Ok, gpu.DisplayAdapter?.UtilizationPercent ?? double.NaN);
            StatusMenu.Header = cpu.UtilizationStatus == MetricStatus.Ok
                && memory.Status == MetricStatus.Ok
                && gpu.OverallStatus == MetricStatus.Ok
                ? "ネイティブ指標: 更新済み"
                : "ネイティブ指標: 未接続（N/A）";
        }
        catch (Exception)
        {
            UpdateMetric("Cpu", false, 0);
            UpdateMetric("Memory", false, 0);
            UpdateMetric("Gpu", false, 0);
            StatusMenu.Header = "ネイティブ指標: 未接続（N/A）";
        }
    }

    internal void StartLiveMonitoring(IMacSensorSession? session = null)
    {
        _session = session ?? new MacSensorSession();
        if (session is null && OperatingSystem.IsMacOS()) _fullScreenDetector = new MacFullScreenDetector();
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += OnTimerTick;
        _timer.Start();
    }

    private async void OnTimerTick(object? sender, EventArgs e)
    {
        if (_disposed) return;
        if (_fullScreenDetector is not null)
            UpdateFullScreenVisibility(_fullScreenDetector.IsForegroundFullScreen(TryGetPlatformHandle()?.Handle ?? IntPtr.Zero));
        await RefreshLiveAsync();
    }

    internal async Task RefreshLiveAsync()
    {
        if (_disposed || _sampling || _session is null || _lifetime.IsCancellationRequested) return;
        _sampling = true;
        try
        {
            SensorHostMetrics metrics = await _session.SampleAsync(_lifetime.Token);
            if (_lifetime.IsCancellationRequested) return;
            _lastMetrics = metrics;
            _diskDetails?.Update(metrics);
            _processDetails?.Update(metrics.HighLoadProcesses);
            UpdateMetric("Cpu", metrics.CpuUtilizationPercent.Status == SensorHostMetricStatus.Ok, metrics.CpuUtilizationPercent.Value ?? double.NaN);
            UpdateMetric("Memory", metrics.MemoryUtilizationPercent.Status == SensorHostMetricStatus.Ok, metrics.MemoryUtilizationPercent.Value ?? double.NaN);
            UpdateMetric("Gpu", metrics.GpuUtilizationPercent.Status == SensorHostMetricStatus.Ok, metrics.GpuUtilizationPercent.Value ?? double.NaN);
            NetworkReceiveText.Text = FormatNetwork(metrics.NetworkInterfaces, receive: true);
            NetworkSendText.Text = FormatNetwork(metrics.NetworkInterfaces, receive: false);
            ReducedNetworkReceiveText.Text = NetworkReceiveText.Text;
            ReducedNetworkSendText.Text = NetworkSendText.Text;
            UpdateNetworkPeaks(metrics.NetworkInterfaces, DateTimeOffset.UtcNow);
            UpdateThermalDetails(metrics, DateTimeOffset.UtcNow);
            MemoryUsageText.Text = FormatMemoryUsage(metrics);
            ToolTip.SetTip(CpuText, FormatLivePercent("CPU", metrics.CpuUtilizationPercent));
            StatusMenu.Header = metrics.MemoryUtilizationPercent.Status == SensorHostMetricStatus.Ok
                ? "1秒周期で更新中（取得不可の指標は N/A）" : "SensorHost: 再接続待ち／取得不可";
        }
        catch (OperationCanceledException) { }
        finally { _sampling = false; }
    }

    internal void SetCpuPeakWindow(int seconds)
    {
        _cpuPeaks.SetWindow(seconds);
        _settings = _settings with { CpuPeakWindowSeconds = seconds };
        CpuPeakText.Text = ReducedCpuPeakText.Text = _cpuPeaks.Text;
        SaveSettings();
    }

    internal void UpdateThermalDetails(SensorHostMetrics metrics, DateTimeOffset now)
    {
        _cpuPeaks.Update(metrics.CpuUtilizationPercent, now);
        CpuPeakText.Text = ReducedCpuPeakText.Text = _cpuPeaks.Text;
        CpuTemperatureText.Text = ReducedCpuTemperatureText.Text = FormatDetail(metrics.CpuTemperatureCelsius, true);
        GpuTemperatureText.Text = ReducedGpuTemperatureText.Text = FormatDetail(metrics.GpuTemperatureCelsius, true);
        GpuPowerText.Text = ReducedGpuPowerText.Text = FormatDetail(metrics.GpuPowerWatts, false);
    }

    internal static string FormatDetail(SensorHostMetricValue metric, bool temperature) =>
        metric.Status == SensorHostMetricStatus.Ok && metric.Value is double value && double.IsFinite(value)
        && (temperature ? value is >= 5 and <= 120 : value is >= 0 and <= 1000)
            ? temperature ? $"{value:0.0}°C*" : $"{value:0.0} W" : "N/A";

    internal void SetNetworkPeakWindow(int seconds)
    {
        _networkPeaks.SetWindow(seconds);
        _settings = _settings with { NetworkPeakWindowSeconds = seconds };
        RenderNetworkPeaks();
        SaveSettings();
    }

    internal void UpdateNetworkPeaks(SensorHostMetricGroup<SensorHostNetworkInterfaceReading> group, DateTimeOffset now)
    {
        _networkPeaks.Update(group, now);
        RenderNetworkPeaks();
    }

    private void RenderNetworkPeaks()
    {
        NetworkPeakLabel.Text = ReducedNetworkPeakLabel.Text = $"{NetworkPeakWindowSeconds}s max";
        NetworkPeakReceiveText.Text = ReducedNetworkPeakReceiveText.Text = _networkPeaks.Receive;
        NetworkPeakSendText.Text = ReducedNetworkPeakSendText.Text = _networkPeaks.Send;
    }

    internal static string FormatNetwork(SensorHostMetricGroup<SensorHostNetworkInterfaceReading> group, bool receive)
    {
        if (group.Status != SensorHostMetricStatus.Ok || group.Values.Length == 0) return "N/A";
        double total = 0;
        foreach (var item in group.Values)
        {
            var metric = receive ? item.ReceiveBytesPerSecond : item.SendBytesPerSecond;
            if (metric.Status == SensorHostMetricStatus.WarmingUp) return "--";
            if (metric.Status != SensorHostMetricStatus.Ok || metric.Value is not double value || !double.IsFinite(value) || value < 0) return "N/A";
            total += value;
        }
        return double.IsFinite(total) ? BytesPerSecondFormatter.Format(total) : "N/A";
    }

    internal static string FormatLivePercent(string label, SensorHostMetricValue metric) =>
        metric.Status == SensorHostMetricStatus.WarmingUp ? $"{label}: 計測準備中" :
        metric.Status == SensorHostMetricStatus.Ok && metric.Value is double value && double.IsFinite(value)
            ? $"{label}: {value:0.0}%" : $"{label}: N/A";

    private async void OnClosed(object? sender, EventArgs e)
    {
        Screens.Changed -= OnScreensChanged;
        Program.Diagnostics.Record(MacDiagnosticEvent.MainWindowClosed);
        _settingsWindow?.Close();
        _scaleInput?.Close();
        _diskDetails?.Close();
        _processDetails?.Close();
        await DisposeAsync();
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        SaveSettings();
        _saveTimer.Stop();
        _disposed = true;
        _timer?.Stop();
        if (_timer is not null) _timer.Tick -= OnTimerTick;
        _session?.StopPolling();
        await _lifetime.CancelAsync();
        if (_session is not null) await _session.DisposeAsync();
        _layerController?.Dispose();
        _layerController = null;
        _cpuSource.Dispose();
        _memorySource.Dispose();
        _gpuSource.Dispose();
        _lifetime.Dispose();
        GC.SuppressFinalize(this);
    }

    internal void UpdateMetric(string name, bool available, double value)
    {
        bool valid = available && double.IsFinite(value) && value >= 0 && value <= 100;
        UpdateMetricVisual(name, valid, value);
        UpdateMetricVisual("Reduced" + name, valid, value);
    }

    private void UpdateMetricVisual(string name, bool valid, double value)
    {
        TextBlock? text = this.FindControl<TextBlock>(name + "Text");
        TextBlock? unit = this.FindControl<TextBlock>(name + "Unit");
        Rectangle? bar = this.FindControl<Rectangle>(name + "Bar");
        if (text is null || unit is null || bar is null) return;
        text.Text = valid ? $"{value:0}" : "N/A";
        unit.Text = valid ? "%" : "";
        ((ScaleTransform)bar.RenderTransform!).ScaleX = valid ? value / 100 : 0;
    }

    internal static string FormatMemoryUsage(SensorHostMetrics metrics)
    {
        double? used = metrics.MemoryUsedBytes.Value, total = metrics.MemoryTotalBytes.Value;
        return metrics.MemoryUsedBytes.Status == SensorHostMetricStatus.Ok
            && metrics.MemoryTotalBytes.Status == SensorHostMetricStatus.Ok
            && used is >= 0 && total is > 0 && used <= total && double.IsFinite(total.Value)
            ? $"{used / 1073741824d:0.0}/{total / 1073741824d:0.0} GiB" : "N/A";
    }

    private ProcessDetailsWindow? _processDetails;
    private MacFullScreenDetector? _fullScreenDetector;
    private bool _hiddenByFullScreen, _suppressHideUntilWindowed;
    internal bool DiagnosticLogging => _settings.DiagnosticLogging;
    internal void ToggleDiagnosticLogging()
    {
        _settings = _settings with { DiagnosticLogging = !DiagnosticLogging };
        if (DiagnosticLogging) Program.Diagnostics.Enabled = true;
        Program.Diagnostics.Record(DiagnosticLogging ? MacDiagnosticEvent.LoggingEnabled : MacDiagnosticEvent.LoggingDisabled);
        Program.Diagnostics.Enabled = DiagnosticLogging;
        SaveSettings();
    }

    private void OnDiagnosticLogging(object? sender, RoutedEventArgs e) => ToggleDiagnosticLogging();

    internal bool HideWhenFullScreen => _settings.HideWhenFullScreen;

    internal void ShowProcessDetails()
    {
        if (_processDetails is not null) { _processDetails.Activate(); return; }
        _processDetails = new ProcessDetailsWindow();
        _processDetails.Update(_lastMetrics.HighLoadProcesses);
        _processDetails.Closed += (_, _) => _processDetails = null;
        _processDetails.Show();
        _processDetails.Activate();
    }

    internal void ShowMonitor()
    {
        _suppressHideUntilWindowed = true;
        _hiddenByFullScreen = false;
        Show();
        Activate();
    }

    internal void ToggleFullScreenHiding()
    {
        _settings = _settings with { HideWhenFullScreen = !HideWhenFullScreen };
        if (!HideWhenFullScreen) UpdateFullScreenVisibility(false);
        SaveSettings();
    }

    internal void UpdateFullScreenVisibility(bool fullScreen)
    {
        if (_disposed) return;
        if (!fullScreen) _suppressHideUntilWindowed = false;
        bool hide = HideWhenFullScreen && fullScreen && !_suppressHideUntilWindowed;
        if (hide && IsVisible) { _hiddenByFullScreen = true; Hide(); Program.Diagnostics.Record(MacDiagnosticEvent.FullScreenHidden); }
        else if (!hide && _hiddenByFullScreen) { _hiddenByFullScreen = false; Show(); Program.Diagnostics.Record(MacDiagnosticEvent.FullScreenRestored); }
    }

    private void OnFullScreenHiding(object? sender, RoutedEventArgs e) => ToggleFullScreenHiding();
    private void OnProcessDetails(object? sender, RoutedEventArgs e) => ShowProcessDetails();

    private DiskDetailsWindow? _diskDetails;
    private SensorHostMetrics _lastMetrics = SensorHostMetrics.Empty;

    internal void ShowDiskDetails()
    {
        if (_diskDetails is not null) { _diskDetails.Activate(); return; }
        _diskDetails = new DiskDetailsWindow();
        _diskDetails.Update(_lastMetrics);
        _diskDetails.Closed += (_, _) => _diskDetails = null;
        _diskDetails.Show();
        _diskDetails.Activate();
    }

    private void OnDiskDetails(object? sender, RoutedEventArgs e) => ShowDiskDetails();

    private MacSettingsWindow? _settingsWindow;
    internal MacSettingsWindow? SettingsWindow => _settingsWindow;

    internal void ShowSettings()
    {
        if (_settingsWindow is not null) { _settingsWindow.Activate(); return; }
        _settingsWindow = new MacSettingsWindow(this);
        _settingsWindow.Closed += (_, _) => _settingsWindow = null;
        _settingsWindow.Show();
        _settingsWindow.Activate();
    }

    private void OnSettings(object? sender, RoutedEventArgs e) => ShowSettings();

    private ScaleInputWindow? _scaleInput;

    internal void ShowScaleInput()
    {
        if (_scaleInput is not null) { _scaleInput.Activate(); return; }
        _scaleInput = new ScaleInputWindow(WidgetScale, SetWidgetScale);
        _scaleInput.Closed += (_, _) => _scaleInput = null;
        _scaleInput.Show();
        _scaleInput.Activate();
    }

    private void OnScaleCustom(object? sender, RoutedEventArgs e) => ShowScaleInput();

    internal void SetWidgetScale(double scale)
    {
        if (!double.IsFinite(scale) || scale is < 0.5 or > 1.5) throw new ArgumentOutOfRangeException(nameof(scale));
        _settings = _settings with { Scale = Math.Round(scale * 100, MidpointRounding.AwayFromZero) / 100 };
        ApplyWidgetLayout();
        SaveSettings();
    }

    internal void SetDisplayMode(WidgetDisplayMode displayMode)
    {
        if (!Enum.IsDefined(displayMode)) throw new ArgumentOutOfRangeException(nameof(displayMode));
        _settings = _settings with { DisplayMode = displayMode };
        ApplyWidgetLayout();
        SaveSettings();
    }

    internal void EnableSettings(IMacWidgetSettingsStore store, IStartupRegistry? startupRegistry = null)
    {
        ArgumentNullException.ThrowIfNull(store);
        _startupRegistry = startupRegistry;
        try
        {
            _settings = store.Load();
            if (_startupRegistry is not null && Environment.ProcessPath is string executable)
                _settings = _settings with { StartAtLogin = _startupRegistry.IsEnabled(executable) };
            Program.Diagnostics.Enabled = _settings.DiagnosticLogging;
            _restoringSettings = true;
            SetWidgetScale(_settings.Scale);
            SetNetworkPeakWindow(_settings.NetworkPeakWindowSeconds);
            SetCpuPeakWindow(_settings.CpuPeakWindowSeconds);
            Topmost = _settings.Topmost;
            _settingsStore = store;
            SettingsMessage = "設定は自動保存されます";
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or JsonException)
        {
            // Preserve unreadable or newer settings instead of overwriting them.
            SettingsMessage = "設定を読み込めません。元ファイルを保持し、保存を停止しています";
        }
        finally { _restoringSettings = false; }
        UpdateSettingsMenu();
    }

    internal void ToggleTopmost()
    {
        Topmost = !Topmost;
        _settings = _settings with { Topmost = Topmost, DesktopLayer = false };
        ApplyLayerMode();
        SaveSettings();
    }

    internal void ToggleDesktopLayer()
    {
        bool enabled = !_settings.DesktopLayer;
        _settings = _settings with { DesktopLayer = enabled, Topmost = false };
        Topmost = false;
        ApplyLayerMode();
        SaveSettings();
    }

    internal void ToggleClickThrough()
    {
        _settings = _settings with { ClickThrough = !_settings.ClickThrough };
        ApplyLayerMode();
        SaveSettings();
    }

    internal void ToggleStartAtLogin()
    {
        if (_startupRegistry is null)
        {
            SettingsMessage = "自動起動を変更できません";
            UpdateSettingsMenu();
            return;
        }

        bool enabled = !_settings.StartAtLogin;
        try
        {
            if (enabled)
            {
                string executable = Environment.ProcessPath ?? throw new IOException("Application path unavailable.");
                _startupRegistry.Enable(executable);
            }
            else
            {
                _startupRegistry.Disable();
            }
            _settings = _settings with { StartAtLogin = enabled };
            SettingsMessage = "設定は自動保存されます";
            SaveSettings();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        {
            SettingsMessage = "自動起動を変更できません";
            UpdateSettingsMenu();
        }
    }

    internal static PixelPoint ClampPosition(PixelPoint desired, PixelRect area, double width, double height, double scaling)
    {
        int maxX = area.X + Math.Max(0, area.Width - (int)Math.Ceiling(width * scaling));
        int maxY = area.Y + Math.Max(0, area.Height - (int)Math.Ceiling(height * scaling));
        return new PixelPoint(Math.Clamp(desired.X, area.X, maxX), Math.Clamp(desired.Y, area.Y, maxY));
    }

    private void OnScreensChanged(object? sender, EventArgs e) => Dispatcher.UIThread.Post(() =>
    {
        if (_disposed) return;
        RestorePosition();
        SaveSettings();
    });

    private void RestorePosition()
    {
        _restoringSettings = true;
        try
        {
            PixelPoint desired = _settings.X is int x && _settings.Y is int y ? new(x, y) : Position;
            var screen = Screens.ScreenFromPoint(desired) ?? Screens.Primary;
            if (screen is not null)
                Position = ClampPosition(desired, screen.WorkingArea, Width, Height, screen.Scaling);
            _positionReady = true;
            _settings = _settings with { X = Position.X, Y = Position.Y };
        }
        finally { _restoringSettings = false; }
    }

    private void SaveSettings()
    {
        _saveTimer.Stop();
        if (_restoringSettings || _disposed) return;
        if (_settingsStore is not null)
        {
            try
            {
                _settingsStore.Save(_settings);
                SettingsMessage = "設定は自動保存されます";
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                SettingsMessage = "設定を保存できません。保存先へのアクセスを確認してください";
            }
        }
        UpdateSettingsMenu();
    }

    private void UpdateSettingsMenu()
    {
        foreach (var entry in _cpuPeakMenus) entry.Item.IsChecked = CpuPeakWindowSeconds == entry.Seconds;
        foreach (var entry in _peakMenus) entry.Item.IsChecked = NetworkPeakWindowSeconds == entry.Seconds;
        SettingsMenu.Header = SettingsMessage;
        TopmostMenu.IsChecked = Topmost;
        DesktopLayerMenu.IsChecked = _settings.DesktopLayer;
        ClickThroughMenu.IsChecked = _settings.ClickThrough;
        StartAtLoginMenu.IsChecked = _settings.StartAtLogin;
        DiagnosticLoggingMenu.IsChecked = DiagnosticLogging;
        FullScreenHideMenu.IsChecked = HideWhenFullScreen;
        Scale50Menu.IsChecked = WidgetScale == 0.5;
        Scale75Menu.IsChecked = WidgetScale == 0.75;
        ScaleNormalMenu.IsChecked = WidgetScale == 1;
        ScaleMediumMenu.IsChecked = WidgetScale == 1.25;
        ScaleLargeMenu.IsChecked = WidgetScale == 1.5;
        DisplayModeStandardMenu.IsChecked = DisplayMode == WidgetDisplayMode.Standard;
        DisplayModeReducedMenu.IsChecked = DisplayMode == WidgetDisplayMode.Reduced;
        WidgetSettingsChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnWidgetPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) BeginMoveDrag(e);
    }

    private void OnScale50(object? sender, RoutedEventArgs e) => SetWidgetScale(0.5);
    private void OnScale75(object? sender, RoutedEventArgs e) => SetWidgetScale(0.75);
    private void OnScaleNormal(object? sender, RoutedEventArgs e) => SetWidgetScale(1);
    private void OnScaleMedium(object? sender, RoutedEventArgs e) => SetWidgetScale(1.25);
    private void OnScaleLarge(object? sender, RoutedEventArgs e) => SetWidgetScale(1.5);
    private void OnDisplayModeStandard(object? sender, RoutedEventArgs e) => SetDisplayMode(WidgetDisplayMode.Standard);
    private void OnDisplayModeReduced(object? sender, RoutedEventArgs e) => SetDisplayMode(WidgetDisplayMode.Reduced);
    private void OnToggleTopmost(object? sender, RoutedEventArgs e) => ToggleTopmost();
    private void OnToggleDesktopLayer(object? sender, RoutedEventArgs e) => ToggleDesktopLayer();
    private void OnToggleClickThrough(object? sender, RoutedEventArgs e) => ToggleClickThrough();
    private void OnToggleStartAtLogin(object? sender, RoutedEventArgs e) => ToggleStartAtLogin();
    private void OnQuit(object? sender, RoutedEventArgs e) => Close();

    private void AttachMacLayer()
    {
        if (!OperatingSystem.IsMacOS() || _layerController is not null) return;
        IntPtr? handle = TryGetPlatformHandle()?.Handle;
        if (handle is not IntPtr nativeHandle || nativeHandle == IntPtr.Zero) return;
        try
        {
            _layerController = new MacWidgetLayerController();
            _layerController.Attach(nativeHandle);
            ApplyLayerMode();
        }
        catch (Exception exception) when (exception is PlatformNotSupportedException or DllNotFoundException or EntryPointNotFoundException or InvalidOperationException)
        {
            _layerController?.Dispose();
            _layerController = null;
        }
    }

    private void ApplyLayerMode()
    {
        if (_layerController is null) return;
        _layerController.SetLayerMode(_settings.DesktopLayer ? WidgetLayerMode.AlwaysOnBottom :
            Topmost ? WidgetLayerMode.AlwaysOnTop : WidgetLayerMode.Normal);
        _layerController.SetClickThrough(_settings.ClickThrough);
    }

    private void ApplyWidgetLayout()
    {
        bool reduced = DisplayMode == WidgetDisplayMode.Reduced;
        double informationWidth = reduced ? ReducedWidgetWidth : StandardWidgetWidth;
        WidgetContent.Width = informationWidth;
        double contentHeight = reduced ? WidgetHeight + 16 : WidgetHeight;
        WidgetContent.Height = contentHeight;
        StandardContentPanel.IsVisible = !reduced;
        ReducedContentPanel.IsVisible = reduced;
        Width = informationWidth * WidgetScale;
        Height = contentHeight * WidgetScale;
        if (_positionReady) RestorePosition();
        UpdateSettingsMenu();
    }
}
