using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using DesktopSystemMonitor.Core.Settings;
using DesktopSystemMonitor.Core.Layout;
using DesktopSystemMonitor.App.Startup;
using MediaColor = System.Windows.Media.Color;
using MediaColorConverter = System.Windows.Media.ColorConverter;
using MediaFontFamily = System.Windows.Media.FontFamily;
using DesktopSystemMonitor.Windows.Window;

namespace DesktopSystemMonitor.App;

[SuppressMessage(
    "Design",
    "CA1001:Types that own disposable fields should be disposable",
    Justification = "WPF Window disposes the TopMost hooks from its Closed event.")]
public partial class MainWindow : Window
{
    private readonly DispatcherTimer _layerRepairTimer;
    private readonly IWindowLayerApi _layerApi;
    private readonly WindowLayerRepairEngine _layerRepair;
    private TopmostWindowRecoveryController? _topmostRecovery;
    private DispatcherOperation? _pendingTopmostRecovery;
    private DispatcherTimer? _topmostRecoveryTimer;
    private long _pendingTopmostRequestedTimestamp;
    private long _lastTopmostRecoveryTimestamp;
    private bool _topmostRecoveryRequestPending;
    private bool _topmostRecoveryInProgress;
    private bool _topmostHealthFailureReported;
    private HwndSource? _hwndSource;
    private IntPtr _sourceHwnd;
    private LayerStrategy _layer = LayerStrategy.BottomMost;
    private bool _applyingLayer;
    private bool _suppressWindowPosRewrite;
    private bool _layerRepairSuspended;
    private bool _closing;
    private bool _forceLayerApplyPending;
    private readonly Action<IntPtr, bool> _applyClickThrough;
    private bool _clickThrough = true;
    private bool _clickThroughApplyPending = true;

    public event Action? LayerFallbackOccurred;
    internal event Action<LayerDiagnosticEvent>? LayerDiagnosticOccurred;
    internal event Action? DisplayConfigurationChanged;
    internal event Action? UserMoveStarted;
    internal event Action? UserMoveCompleted;

    public MainWindow()
        : this(NativeWindowLayerApi.Instance)
    {
    }

    internal MainWindow(IWindowLayerApi layerApi)
        : this(layerApi, ClickThroughHelper.SetClickThrough)
    {
    }

    internal MainWindow(IWindowLayerApi layerApi, Action<IntPtr, bool> applyClickThrough)
    {
        _layerApi = layerApi ?? throw new ArgumentNullException(nameof(layerApi));
        _applyClickThrough = applyClickThrough ?? throw new ArgumentNullException(nameof(applyClickThrough));
        _layerRepair = new WindowLayerRepairEngine(_layerApi, PublishLayerDiagnostic);
        InitializeComponent();
        SourceInitialized += OnSourceInitialized;
        _layerRepairTimer = new DispatcherTimer(TimeSpan.FromSeconds(2), DispatcherPriority.Background, (_, _) =>
        {
            RunLayerRepairTick();
        }, Dispatcher);
        Closed += OnClosed;
        IsVisibleChanged += OnIsVisibleChanged;
        MouseLeftButtonDown += OnMouseLeftButtonDown;
    }

    public MetricViewModel? ViewModel
    {
        get => DataContext as MetricViewModel;
        set => DataContext = value;
    }

    public LayerStrategy Layer
    {
        get => _layer;
        set => SetLayer(value, LayerRepairTrigger.LayerSetter);
    }

    public bool ClickThrough
    {
        get => _clickThrough;
        set
        {
            Dispatcher.VerifyAccess();
            if (_clickThrough == value && !_clickThroughApplyPending)
            {
                return;
            }
            _clickThrough = value;
            _clickThroughApplyPending = true;
            TryApplyClickThrough(LayerRepairTrigger.ClickThroughSetter);
        }
    }

    public IntPtr Handle => new WindowInteropHelper(this).EnsureHandle();

    public void ReapplyWindowStyles()
    {
        ReapplyWindowStyles(LayerRepairTrigger.ExplicitReapply);
    }

    internal void ReapplyWindowStyles(LayerRepairTrigger trigger)
    {
        Dispatcher.VerifyAccess();
        if (_sourceHwnd == IntPtr.Zero || _closing || !IsVisible || _layerRepairSuspended)
        {
            if (!IsVisible)
            {
                _layerRepair.ResetObservationEpisode();
            }
            return;
        }
        TryApplyClickThrough(trigger);
        _ = RepairLayerOnce(trigger, countBottomMostFailure: false);
        if (Layer == LayerStrategy.TopMost)
        {
            RequestTopmostRecovery();
        }
    }

    internal void SetLayer(LayerStrategy value, LayerRepairTrigger trigger)
    {
        Dispatcher.VerifyAccess();
        bool strategyChanged = _layer != value;
        if (strategyChanged)
        {
            _layerRepair.ResetEpisode();
            _layer = value;
            _forceLayerApplyPending = true;
        }
        Topmost = value == LayerStrategy.TopMost;
        UpdateTopmostRecoveryEnabled();
        if (_sourceHwnd != IntPtr.Zero && !_closing)
        {
            _ = RepairLayerOnce(
                trigger,
                countBottomMostFailure: false,
                forceApply: strategyChanged);
        }
    }

    internal void SetLayerRepairSuspended(bool suspended)
    {
        Dispatcher.VerifyAccess();
        if (_layerRepairSuspended == suspended)
        {
            return;
        }
        _layerRepairSuspended = suspended;
        _layerRepair.ResetObservationEpisode();
        UpdateTopmostRecoveryEnabled();
        if (!suspended)
        {
            RequestTopmostRecovery();
        }
    }

    internal LayerRepairResult RunLayerRepairTick()
    {
        Dispatcher.VerifyAccess();
        if (_closing
            || _sourceHwnd == IntPtr.Zero
            || Dispatcher.HasShutdownStarted
            || Dispatcher.HasShutdownFinished)
        {
            _layerRepair.ResetEpisode();
            _forceLayerApplyPending = false;
            return SkippedRepairResult();
        }
        if (_layerRepairSuspended || !IsVisible)
        {
            _layerRepair.ResetObservationEpisode();
            return SkippedRepairResult();
        }

        return RepairLayerOnce(LayerRepairTrigger.Timer, countBottomMostFailure: true);
    }

    public void ApplyVisualSettings(AppSettings settings, bool batteryPresent = false)
    {
        ArgumentNullException.ThrowIfNull(settings);
        AppSettings normalized = settings.Normalized();
        WidgetDisplayMode displayMode = normalized.DisplayMode;
        bool reduced = displayMode == WidgetDisplayMode.Reduced;
        double informationWidth = WidgetWidthCalculator.GetInformationWidth(displayMode);
        double scale = normalized.UiScalePercent / 100d;
        WidgetRoot.Width = informationWidth;
        WidgetRoot.LayoutTransform = new ScaleTransform(scale, scale);
        StandardContentPanel.Visibility = reduced ? Visibility.Collapsed : Visibility.Visible;
        ReducedContentPanel.Visibility = reduced ? Visibility.Visible : Visibility.Collapsed;
        ApplyRowVisibility(normalized, batteryPresent);
        CpuTemperatureColumn.Width = new GridLength(normalized.ShowCpuMetrics && normalized.EffectiveShowCpuTemperature ? 44d : 0d);
        GpuTemperatureColumn.Width = new GridLength(normalized.ShowGpuMetrics && normalized.EffectiveShowGpuTemperature ? 44d : 0d);
        bool showReducedNetworkPeaks = reduced && normalized.EffectiveShowNetworkPeaks;
        ReducedNetworkRow.Height = showReducedNetworkPeaks ? 64d : 48d;
        ReducedNetworkRateReceiveRowDefinition.Height = new GridLength(showReducedNetworkPeaks ? 24d : 29d);
        ReducedNetworkRateSendRowDefinition.Height = new GridLength(showReducedNetworkPeaks ? 16d : 14d);
        ReducedNetworkPeakReceiveRowDefinition.Height = new GridLength(showReducedNetworkPeaks ? 12d : 0d);
        ReducedNetworkPeakSendRowDefinition.Height = new GridLength(showReducedNetworkPeaks ? 12d : 5d);
        Visibility reducedNetworkPeakVisibility = showReducedNetworkPeaks ? Visibility.Visible : Visibility.Collapsed;
        ReducedNetworkPeakPanel.Visibility = reducedNetworkPeakVisibility;
        ReducedNetworkPeakReceiveRate.Visibility = reducedNetworkPeakVisibility;
        ReducedNetworkPeakSendRate.Visibility = reducedNetworkPeakVisibility;
        FrameworkElement[] orderedRows = reduced
            ? [ReducedCpuRow, ReducedMemoryRow, ReducedGpuRow, ReducedDiskRow, ReducedNetworkRow, ReducedBatteryRow]
            : [CpuRow, MemoryRow, GpuRow, DiskRow, NetworkRow, BatteryRow];
        FrameworkElement[] visibleRows = orderedRows.Where(row => row.Visibility == Visibility.Visible).ToArray();
        double informationHeight = WidgetHeightCalculator.Calculate(visibleRows.Length)
            + (showReducedNetworkPeaks ? ReducedNetworkRow.Height - WidgetHeightCalculator.RowHeight : 0d);
        WidgetRoot.Height = informationHeight;
        BackgroundPresentationMode backgroundMode = BackgroundPresentationPolicy.Evaluate(normalized);
        double fadeExpansionRatio = backgroundMode == BackgroundPresentationMode.None
            ? 0
            : BackgroundBrushFactory.EdgeFadeExpansionRatio(normalized);
        Width = informationWidth * scale * (1d + (2d * fadeExpansionRatio));
        Height = informationHeight * scale * (1d + (2d * fadeExpansionRatio));
        Visibility metricPeakVisibility = !reduced && normalized.ShowRecentPeaks ? Visibility.Visible : Visibility.Collapsed;
        CpuPeakMarker.Visibility = metricPeakVisibility;
        MemoryPeakMarker.Visibility = metricPeakVisibility;
        GpuPeakMarker.Visibility = metricPeakVisibility;
        DiskPeakMarker.Visibility = metricPeakVisibility;
        NetworkPeakRow.Visibility = !reduced && normalized.EffectiveShowNetworkPeaks ? Visibility.Visible : Visibility.Collapsed;
        FontFamily = new MediaFontFamily(normalized.FontFamilyName);
        Resources["WidgetForeground"] = new SolidColorBrush((MediaColor)MediaColorConverter.ConvertFromString(normalized.ForegroundColor));
        Resources["WidgetMuted"] = new SolidColorBrush((MediaColor)MediaColorConverter.ConvertFromString(normalized.MutedColor));
        Resources["WidgetAccent"] = new SolidColorBrush((MediaColor)MediaColorConverter.ConvertFromString(normalized.AccentColor));
        Resources["WidgetRxAccent"] = new SolidColorBrush((MediaColor)MediaColorConverter.ConvertFromString(normalized.RxAccentColor));
        Resources["WidgetTxAccent"] = new SolidColorBrush((MediaColor)MediaColorConverter.ConvertFromString(normalized.TxAccentColor));
        if (backgroundMode == BackgroundPresentationMode.Inline)
        {
            BackgroundBrushFactory.Apply(BackgroundFadeHost, BackgroundSurface, normalized);
        }
        else
        {
            BackgroundBrushFactory.Clear(BackgroundFadeHost, BackgroundSurface);
        }
        Opacity = 1d;
    }

    private void ApplyRowVisibility(AppSettings settings, bool batteryPresent)
    {
        bool[] visible =
        [
            settings.ShowCpuMetrics,
            true,
            settings.ShowGpuMetrics,
            settings.ShowDiskMetrics,
            true,
            settings.ShowBatteryEstimate && batteryPresent,
        ];
        FrameworkElement[][] rows =
        [
            [CpuRow, ReducedCpuRow],
            [MemoryRow, ReducedMemoryRow],
            [GpuRow, ReducedGpuRow],
            [DiskRow, ReducedDiskRow],
            [NetworkRow, ReducedNetworkRow],
            [BatteryRow, ReducedBatteryRow],
        ];
        int visibleIndex = 0;
        for (int index = 0; index < rows.Length; index++)
        {
            Visibility rowVisibility = visible[index] ? Visibility.Visible : Visibility.Collapsed;
            double topMargin = visible[index] && visibleIndex++ > 0 ? WidgetHeightCalculator.RowGap : 0d;
            foreach (FrameworkElement row in rows[index])
            {
                row.Visibility = rowVisibility;
                row.Margin = new Thickness(0, topMargin, 0, 0);
            }
        }
    }

    internal void SetInlineBackground(AppSettings settings, BackgroundPresentationMode mode)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (mode == BackgroundPresentationMode.Inline)
        {
            BackgroundBrushFactory.Apply(BackgroundFadeHost, BackgroundSurface, settings);
        }
        else
        {
            BackgroundBrushFactory.Clear(BackgroundFadeHost, BackgroundSurface);
        }
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        Dispatcher.VerifyAccess();
        _sourceHwnd = new WindowInteropHelper(this).Handle;
        if (_sourceHwnd == IntPtr.Zero)
        {
            return;
        }
        _hwndSource = HwndSource.FromHwnd(_sourceHwnd);
        _hwndSource?.AddHook(WindowProc);
        TryApplyClickThrough(LayerRepairTrigger.SourceInitialized);
        _ = RepairLayerOnce(LayerRepairTrigger.SourceInitialized, countBottomMostFailure: false);
        _topmostRecovery = new TopmostWindowRecoveryController(_sourceHwnd);
        _topmostRecovery.RecoveryRequested += RequestTopmostRecovery;
        _topmostRecovery.HealthChanged += OnTopmostRecoveryHealthChanged;
        UpdateTopmostRecoveryEnabled();
        RequestTopmostRecovery();
        _layerRepairTimer.Start();
    }

    private const int WM_WINDOWPOSCHANGING = 0x0046;
    private const int WM_ACTIVATE = 0x0006;
    private const int WM_SETTINGCHANGE = 0x001A;
    private const int WM_DISPLAYCHANGE = 0x007E;
    private const int SPI_SETWORKAREA = 0x002F;

    private IntPtr WindowProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == BottomMostStrategy.TaskbarCreatedMessage)
        {
            ReapplyWindowStyles(LayerRepairTrigger.TaskbarCreated);
            return IntPtr.Zero;
        }
        switch (msg)
        {
            case WM_WINDOWPOSCHANGING:
                _ = RewriteWindowPositionForLayer(lParam);
                break;
            case WM_ACTIVATE:
                _ = RepairLayerOnce(LayerRepairTrigger.WindowActivated, countBottomMostFailure: false);
                RequestTopmostRecovery();
                break;
            case WM_DISPLAYCHANGE:
                ReapplyWindowStyles(LayerRepairTrigger.DisplayChanged);
                DisplayConfigurationChanged?.Invoke();
                break;
            case WM_SETTINGCHANGE:
                if (IsDisplayConfigurationChangeMessage(msg, wParam))
                {
                    DisplayConfigurationChanged?.Invoke();
                }
                break;
        }
        return IntPtr.Zero;
    }

    internal static bool IsDisplayConfigurationChangeMessage(int message, IntPtr wParam) =>
        message == WM_DISPLAYCHANGE
        || (message == WM_SETTINGCHANGE && wParam.ToInt64() == SPI_SETWORKAREA);

    private LayerRepairResult RepairLayerOnce(
        LayerRepairTrigger trigger,
        bool countBottomMostFailure,
        bool forceApply = false)
    {
        Dispatcher.VerifyAccess();
        if (_applyingLayer
            || _layerRepairSuspended
            || _closing
            || _sourceHwnd == IntPtr.Zero
            || (trigger != LayerRepairTrigger.SourceInitialized && !IsVisible))
        {
            if (!IsVisible)
            {
                _layerRepair.ResetObservationEpisode();
            }
            return SkippedRepairResult();
        }

        LayerRepairResult result;
        try
        {
            _applyingLayer = true;
            bool applyRequired = forceApply || _forceLayerApplyPending;
            result = _layerRepair.Repair(
                _sourceHwnd,
                Layer,
                trigger,
                countBottomMostFailure,
                applyRequired);
            _forceLayerApplyPending = false;
        }
        catch
        {
            result = _layerRepair.RecordManagedException(Layer, trigger);
        }
        finally
        {
            _applyingLayer = false;
        }

        if (result.Outcome == LayerRepairOutcome.FallbackPending)
        {
            ExecuteBottomMostFallback(trigger);
        }
        return result;
    }

    internal bool RewriteWindowPositionForLayer(IntPtr windowPosPointer)
    {
        Dispatcher.VerifyAccess();
        if (_layerRepairSuspended || _closing || _sourceHwnd == IntPtr.Zero)
        {
            return false;
        }

        try
        {
            return BottomMostStrategy.RewriteWindowPosForLayer(
                windowPosPointer,
                Layer,
                _suppressWindowPosRewrite,
                _layerApi);
        }
        catch
        {
            _layerRepair.RecordNonRepairManagedException(
                Layer,
                LayerRepairTrigger.WindowPositionChanging,
                LayerFailureKind.HookObservationException);
            return false;
        }
    }

    private static readonly long TopmostRecoveryMinIntervalTicks =
        (Stopwatch.Frequency * 250L) / 1000L;

    private void UpdateTopmostRecoveryEnabled()
    {
        if (_topmostRecovery is null)
        {
            return;
        }

        bool shouldEnable = Layer == LayerStrategy.TopMost
            && !_layerRepairSuspended
            && !_closing
            && _sourceHwnd != IntPtr.Zero
            && IsVisible;
        _topmostRecovery.SetEnabled(shouldEnable);
        if (!shouldEnable)
        {
            AbortPendingTopmostRecovery();
        }
    }

    private void RequestTopmostRecovery()
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(DispatcherPriority.Background, RequestTopmostRecovery);
            return;
        }

        if (_topmostRecovery is null
            || Layer != LayerStrategy.TopMost
            || _layerRepairSuspended
            || _closing
            || _sourceHwnd == IntPtr.Zero
            || !IsVisible)
        {
            return;
        }

        if (_topmostRecoveryInProgress || _topmostRecoveryRequestPending)
        {
            return;
        }

        _pendingTopmostRequestedTimestamp = Stopwatch.GetTimestamp();
        _topmostRecoveryRequestPending = true;
        ScheduleTopmostRecovery();
    }

    private void ScheduleTopmostRecovery()
    {
        if (!_topmostRecoveryRequestPending
            || _pendingTopmostRecovery is not null
            || _topmostRecoveryTimer is not null)
        {
            return;
        }

        long now = Stopwatch.GetTimestamp();
        long eligibleAt = Math.Max(
            _pendingTopmostRequestedTimestamp,
            _lastTopmostRecoveryTimestamp + TopmostRecoveryMinIntervalTicks);
        if (now < eligibleAt)
        {
            _topmostRecoveryTimer = new DispatcherTimer(
                DispatcherPriority.Background,
                Dispatcher)
            {
                Interval = StopwatchTicksToTimeSpan(eligibleAt - now),
            };
            _topmostRecoveryTimer.Tick += OnTopmostRecoveryTimerTick;
            _topmostRecoveryTimer.Start();
            return;
        }

        _pendingTopmostRecovery = Dispatcher.BeginInvoke(
            DispatcherPriority.Background,
            ExecuteTopmostRecovery);
    }

    private void OnTopmostRecoveryTimerTick(object? sender, EventArgs e)
    {
        _topmostRecoveryTimer?.Stop();
        _topmostRecoveryTimer = null;
        ScheduleTopmostRecovery();
    }

    private void ExecuteTopmostRecovery()
    {
        _pendingTopmostRecovery = null;
        if (!_topmostRecoveryRequestPending)
        {
            return;
        }

        _topmostRecoveryRequestPending = false;
        if (_topmostRecovery is null
            || Layer != LayerStrategy.TopMost
            || _layerRepairSuspended
            || _closing
            || _sourceHwnd == IntPtr.Zero
            || !IsVisible)
        {
            return;
        }

        long now = Stopwatch.GetTimestamp();
        long eligibleAt = Math.Max(
            _pendingTopmostRequestedTimestamp,
            _lastTopmostRecoveryTimestamp + TopmostRecoveryMinIntervalTicks);
        if (now < eligibleAt)
        {
            _topmostRecoveryRequestPending = true;
            ScheduleTopmostRecovery();
            return;
        }

        _lastTopmostRecoveryTimestamp = now;
        _topmostRecoveryInProgress = true;
        try
        {
            _ = _topmostRecovery.TryRecover();
        }
        catch
        {
            // A transient user32 failure is reported by the controller and a
            // later WinEvent or lifecycle trigger will retry the operation.
        }
        finally
        {
            _topmostRecoveryInProgress = false;
        }
    }

    private void AbortPendingTopmostRecovery()
    {
        _pendingTopmostRecovery?.Abort();
        _pendingTopmostRecovery = null;
        _topmostRecoveryTimer?.Stop();
        _topmostRecoveryTimer = null;
        _pendingTopmostRequestedTimestamp = 0;
        _topmostRecoveryRequestPending = false;
    }

    private static TimeSpan StopwatchTicksToTimeSpan(long ticks) =>
        TimeSpan.FromSeconds(ticks / (double)Stopwatch.Frequency);

    private void OnTopmostRecoveryHealthChanged(TopmostRecoveryHealth health)
    {
        if (!health.IsDegraded)
        {
            _topmostHealthFailureReported = false;
            return;
        }

        if (_topmostHealthFailureReported)
        {
            return;
        }

        _topmostHealthFailureReported = true;
        LayerFailureKind failureKind = string.Equals(
            health.Operation,
            "SetWinEventHook",
            StringComparison.Ordinal)
            ? LayerFailureKind.TopmostHookRegistrationFailed
            : LayerFailureKind.TopmostRecoveryFailed;
        PublishLayerDiagnostic(new LayerDiagnosticEvent(
            LayerDiagnosticCategory.TopmostRecoveryFailure,
            LayerStrategy.TopMost,
            LayerEffectiveState.Unknown,
            LayerRepairTrigger.TopmostRecovery,
            failureKind,
            health.ErrorCode == 0 ? null : health.ErrorCode,
            health.ConsecutiveFailures));
    }

    private void ExecuteBottomMostFallback(LayerRepairTrigger trigger)
    {
        if (_applyingLayer
            || _layerRepairSuspended
            || _closing
            || _sourceHwnd == IntPtr.Zero
            || Layer != LayerStrategy.BottomMost)
        {
            return;
        }

        try
        {
            _applyingLayer = true;
            try
            {
                _suppressWindowPosRewrite = true;
                LayerApplyResult normalResult = WindowLayerOperation.Apply(
                    _layerApi,
                    _sourceHwnd,
                    LayerStrategy.Normal);
                if (!normalResult.Succeeded)
                {
                    _ = _layerRepair.RecordFallbackFailure(normalResult, trigger);
                    return;
                }

                _layer = LayerStrategy.Normal;
                Topmost = false;
                _layerRepair.ResetEpisode();
                _forceLayerApplyPending = false;
            }
            catch
            {
                _ = _layerRepair.RecordManagedException(Layer, trigger);
                return;
            }
            finally
            {
                _suppressWindowPosRewrite = false;
            }
        }
        finally
        {
            _applyingLayer = false;
        }

        PublishLayerFallback(trigger);
    }

    private void PublishLayerFallback(LayerRepairTrigger trigger)
    {
        Delegate[] subscribers = LayerFallbackOccurred?.GetInvocationList() ?? [];
        foreach (Action subscriber in subscribers.Cast<Action>())
        {
            try
            {
                subscriber();
            }
            catch
            {
                _layerRepair.RecordFallbackSubscriberFailure(trigger);
            }
        }
    }

    private void PublishLayerDiagnostic(LayerDiagnosticEvent diagnosticEvent)
    {
        Delegate[] subscribers = LayerDiagnosticOccurred?.GetInvocationList() ?? [];
        foreach (Action<LayerDiagnosticEvent> subscriber in subscribers.Cast<Action<LayerDiagnosticEvent>>())
        {
            try
            {
                subscriber(diagnosticEvent);
            }
            catch
            {
                // A diagnostic sink cannot destabilize the WPF message loop.
            }
        }
    }

    private LayerRepairResult SkippedRepairResult() => new(
        LayerRepairOutcome.Skipped,
        new LayerApplyResult(Layer, false, null, 0, LayerFailureKind.None),
        0);

    private void TryApplyClickThrough(LayerRepairTrigger trigger)
    {
        if (!_clickThroughApplyPending
            || _sourceHwnd == IntPtr.Zero
            || _closing
            || !IsVisible
            || _layerRepairSuspended)
        {
            return;
        }

        try
        {
            _applyClickThrough(_sourceHwnd, _clickThrough);
            _clickThroughApplyPending = false;
        }
        catch
        {
            _layerRepair.RecordNonRepairManagedException(
                Layer,
                trigger,
                LayerFailureKind.StyleMutationException);
        }
    }

    private void OnIsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (!IsVisible)
        {
            AbortPendingTopmostRecovery();
            UpdateTopmostRecoveryEnabled();
            _layerRepair.ResetObservationEpisode();
        }
        else
        {
            TryApplyClickThrough(LayerRepairTrigger.VisibilityChanged);
            UpdateTopmostRecoveryEnabled();
            RequestTopmostRecovery();
        }
    }

    private void OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (!ClickThrough && e.LeftButton == MouseButtonState.Pressed)
        {
            UserMoveStarted?.Invoke();
            try
            {
                DragMove();
            }
            finally
            {
                UserMoveCompleted?.Invoke();
            }
        }
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _closing = true;
        _layerRepairTimer.Stop();
        AbortPendingTopmostRecovery();
        _hwndSource?.RemoveHook(WindowProc);
        _hwndSource = null;
        _sourceHwnd = IntPtr.Zero;
        if (_topmostRecovery is not null)
        {
            _topmostRecovery.RecoveryRequested -= RequestTopmostRecovery;
            _topmostRecovery.HealthChanged -= OnTopmostRecoveryHealthChanged;
            _topmostRecovery.Dispose();
            _topmostRecovery = null;
        }
        _layerRepair.ResetEpisode();
        _forceLayerApplyPending = false;
    }
}
