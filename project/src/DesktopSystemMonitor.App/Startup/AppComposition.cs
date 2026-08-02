using System.IO;
using System.Collections.Immutable;
using DesktopSystemMonitor.App.Tray;
using DesktopSystemMonitor.Core.Metrics;
using DesktopSystemMonitor.Core.Sampling;
using DesktopSystemMonitor.Core.Settings;
using DesktopSystemMonitor.Core.Utility;
using DesktopSystemMonitor.Windows.Cpu;
using DesktopSystemMonitor.Windows.Gpu;
using DesktopSystemMonitor.Windows.Memory;
using DesktopSystemMonitor.Windows.Network;
using DesktopSystemMonitor.Windows.Power;
using DesktopSystemMonitor.Windows.Disk;
using DesktopSystemMonitor.Windows.Battery;
using DesktopSystemMonitor.Windows.Startup;
using DesktopSystemMonitor.Windows.Window;
using Application = System.Windows.Application;
using MessageBox = System.Windows.MessageBox;
using MessageBoxButton = System.Windows.MessageBoxButton;
using MessageBoxImage = System.Windows.MessageBoxImage;
using Microsoft.Win32;
using System.Windows.Threading;
using System.Globalization;
using DesktopSystemMonitor.App.Diagnostics;

namespace DesktopSystemMonitor.App.Startup;

/// <summary>
/// Wires up the metric sources, sampling orchestrator, view-model, tray icon,
/// window layer strategy, and settings store for the widget. Owns all
/// resources so <c>OnExit</c> can dispose the tree deterministically.
/// </summary>
public sealed class AppComposition : IDisposable
{
    private const string MutexName = "Local\\DesktopSystemMonitor.SingleInstance";
    private const string ActivationEventName = "Local\\DesktopSystemMonitor.ShowSettings";

    private readonly ISettingsStore _settingsStore;
    private readonly IStartupRegistry _startupRegistry;
    private readonly Func<IntPtr, bool> _isForegroundFullScreen;
    private IMetricSource<CpuSnapshot>? _cpu;
    private IMetricSource<MemorySnapshot>? _memory;
    private IGpuMetricSource? _gpu;
    private INetworkMetricSource? _network;
    private IPowerMetricSource? _power;
    private IMetricSource<TemperatureSnapshot>? _temperature;
    private IDiskMetricSource? _disk;
    private IBatteryMetricSource? _battery;
    private readonly MetricViewModel _viewModel = new();
    private readonly BatteryChargeStateCoordinator _batteryChargeState;
    private NotifyIconController? _tray;
    private HighLoadProcessesWindow? _processWindow;
    private MetricSnapshot? _latestSnapshot;
    private bool _batteryPresent;

    private System.Threading.Mutex? _mutex;
    private EventWaitHandle? _activationEvent;
    private readonly CancellationTokenSource _activationCts = new();
    private Task? _activationListener;
    private bool _mutexOwned;
    private SamplingOrchestrator? _sampler;
    private MainWindow? _mainWindow;
    private SettingsWindow? _settingsWindow;
    private bool _settingsFlowActive;
    private AppSettings _settings;
    private MetricSnapshotSampler? _snapshotSampler;
    private DispatcherTimer? _fullscreenTimer;
    private DispatcherTimer? _batteryReflowTimer;
    private bool _pendingBatteryPresent;
    private WindowPlacementController? _windowPlacement;
    private BackgroundLayerCoordinator? _backgroundLayer;
    private LayerRepairSuspensionCoordinator? _layerRepairSuspension;
    private bool _sessionLocked;
    private readonly FullScreenAutoHideCoordinator _fullScreenAutoHide = new();
    private readonly SamplingSuspensionCoordinator _samplingSuspension = new();
    private readonly DiagnosticLog _diagnostics;
    private readonly ShutdownGate _shutdownGate = new();

    private AppComposition(
        ISettingsStore settingsStore,
        IStartupRegistry startupRegistry,
        Func<IntPtr, bool>? isForegroundFullScreen = null)
    {
        _settingsStore = settingsStore;
        _startupRegistry = startupRegistry;
        _isForegroundFullScreen = isForegroundFullScreen ?? FullScreenDetector.IsForegroundFullScreen;
        _settings = _settingsStore.Load();
        string logDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "DesktopSystemMonitor",
            "logs");
        _diagnostics = new DiagnosticLog(logDirectory, _settings.DiagnosticLoggingEnabled);
        _batteryChargeState = new BatteryChargeStateCoordinator(
            _settingsStore,
            (category, exception) => _diagnostics.Record(category, exception));
    }

    public static AppComposition Create()
    {
        var settingsStore = new FileSystemSettingsStore(FileSystemSettingsStore.DefaultPath());
        var startup = new StartupRegistryService();
        return new AppComposition(settingsStore, startup);
    }

    public bool TryClaimSingleInstance()
    {
        _mutex = new System.Threading.Mutex(initiallyOwned: true, MutexName, out bool createdNew);
        _mutexOwned = createdNew;
        if (createdNew)
        {
            _activationEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ActivationEventName);
            _activationListener = Task.Run(ListenForActivation);
        }
        return createdNew;
    }

    public static void SignalExistingInstance()
    {
        for (int attempt = 0; attempt < 10; attempt++)
        {
            try
            {
                using EventWaitHandle existing = EventWaitHandle.OpenExisting(ActivationEventName);
                existing.Set();
                return;
            }
            catch (WaitHandleCannotBeOpenedException)
            {
                // The first instance may still be between mutex creation and event creation.
                Thread.Sleep(50);
            }
        }
    }

    public void StartMainWindow()
    {
        _cpu = CreateSource<CpuSnapshot>(() => new CpuMetricSource(), CpuSnapshot.Unavailable);
        _memory = CreateSource<MemorySnapshot>(() => new MemoryMetricSource(), MemorySnapshot.Unavailable);
        _gpu = CreateGpuSource();
        _gpu.SetPreferredAdapter(ParseGpuLuid(_settings.PinnedGpuLuidHex));
        _network = CreateNetworkSource();
        _network.SetSelectedAdapters(_settings.SelectedNetworkAdapterLuids);
        _power = CreatePowerSource();
        _temperature = _power as IMetricSource<TemperatureSnapshot>
            ?? new UnavailableMetricSource<TemperatureSnapshot>(TemperatureSnapshot.Unavailable);
        _disk = CreateDiskSource();
        _disk.SetEnabled(_settings.ShowDiskMetrics, _settings.SelectedPhysicalDiskNumber);
        _battery = CreateBatterySource();
        _battery.SetEnabled(_settings.ShowBatteryEstimate);
        if (_settings.ShowBatteryEstimate)
        {
            ValueTask<BatterySnapshot> probe = _battery.SampleAsync(default);
            if (StartupBatteryProbe.TryConsume(probe, _diagnostics.Record, out BatterySnapshot initialBattery))
            {
                _batteryPresent = initialBattery.BatteryPresent && initialBattery.PowerState != BatteryPowerState.Absent;
            }
        }
        _snapshotSampler = new MetricSnapshotSampler(
            _cpu,
            _memory,
            _gpu,
            _network,
            _power,
            new SystemClock(),
            (category, exception) => _diagnostics.Record(category, exception),
            _disk,
            _battery,
            _temperature);
        ReconcileStartupSetting();
        _mainWindow = new MainWindow
        {
            ViewModel = _viewModel,
            Layer = MapLayer(_settings.LayerMode),
            ClickThrough = _settings.ClickThrough,
        };
        _viewModel.ApplyNetworkPeakWindow(_settings.NetworkPeakWindowSeconds);
        _viewModel.ApplyMetricVisibility(_settings.ShowCpuMetrics, _settings.ShowGpuMetrics);
        _mainWindow.LayerFallbackOccurred += OnLayerFallback;
        _mainWindow.LayerDiagnosticOccurred += _diagnostics.RecordLayerEvent;
        _mainWindow.ApplyVisualSettings(_settings, _batteryPresent);
        _backgroundLayer = new BackgroundLayerCoordinator(
            _mainWindow,
            _diagnostics.Record,
            Application.Current.Dispatcher);
        _backgroundLayer.Apply(_settings, LayerRepairTrigger.SourceInitialized);
        _layerRepairSuspension = new LayerRepairSuspensionCoordinator(
            suspended => _mainWindow?.SetLayerRepairSuspended(suspended),
            trigger => _mainWindow?.ReapplyWindowStyles(trigger),
            (suspended, trigger) => _backgroundLayer?.SetRepairSuspended(suspended, trigger),
            _diagnostics.Record);
        _windowPlacement = new WindowPlacementController(
            _mainWindow,
            () => _settings,
            UpdateSettings,
            _diagnostics.Record,
            Application.Current.Dispatcher);
        _windowPlacement.PositionWindow();
        _backgroundLayer.SyncNow();
        _batteryReflowTimer = new DispatcherTimer(
            TimeSpan.FromMilliseconds(500),
            DispatcherPriority.Background,
            (_, _) => ApplyPendingBatteryCapability(),
            Application.Current.Dispatcher);
        _batteryReflowTimer.Stop();
        _tray = new NotifyIconController();
        HookTray();
        _tray?.SetState(_settings.ClickThrough, _settings.StartWithWindows, MapLayer(_settings.LayerMode));
        _tray?.SetDisplayMode(_settings.DisplayMode);
        _tray?.SetHighLoadProcessMenuEnabled(_settings.EnableHighLoadProcessDetails);
        _mainWindow.Show();
        _mainWindow.ReapplyWindowStyles();
        _backgroundLayer.Reapply(LayerRepairTrigger.ExplicitReapply);

        StartSampler();
        SystemEvents.SessionSwitch += OnSessionSwitch;
        _fullscreenTimer = new DispatcherTimer(TimeSpan.FromSeconds(1), DispatcherPriority.Background, (_, _) => CheckFullScreen(), Application.Current.Dispatcher);
        _fullscreenTimer.Start();
    }

    private void StartSampler()
    {
        var interval = TimeSpan.FromSeconds(_settings.SamplingIntervalSeconds);
        _sampler = new SamplingOrchestrator(
            sample: SampleAllAsync,
            onSnapshot: PublishSnapshot,
            onError: exception => _diagnostics.Record("sampling-failure", exception),
            interval: interval);
        _sampler.Start();
    }

    private void HookTray()
    {
        if (_tray is null)
        {
            throw new InvalidOperationException("Tray controller has not been created.");
        }
        _tray.ExitRequested += RequestExit;
        _tray.ClickThroughToggled += clickThrough =>
        {
            if (!CanEditSettings())
            {
                _tray.SetState(_settings.ClickThrough, _settings.StartWithWindows, MapLayer(_settings.LayerMode));
                return;
            }
            UpdateSettings(s => s with { ClickThrough = clickThrough });
            if (_mainWindow is not null)
            {
                _mainWindow.ClickThrough = clickThrough;
                _mainWindow.ReapplyWindowStyles();
            }
        };
        _tray.DisplayModeRequested += ApplyDisplayModeFromTray;
        _tray.StartupToggled += enable =>
        {
            if (!CanEditSettings())
            {
                _tray.SetState(_settings.ClickThrough, _settings.StartWithWindows, MapLayer(_settings.LayerMode));
                return;
            }
            bool changed = false;
            try
            {
                string path = GetExecutablePath();
                if (enable)
                {
                    _startupRegistry.Enable(path);
                }
                else
                {
                    _startupRegistry.Disable();
                }
                changed = true;
            }
            catch
            {
                // don't disturb the widget for a failed registry write
                _diagnostics.Record("startup-registration-failure");
            }
            if (changed)
            {
                UpdateSettings(s => s with { StartWithWindows = enable });
            }
            _tray.SetState(_settings.ClickThrough, _settings.StartWithWindows, MapLayer(_settings.LayerMode));
        };
        _tray.LayerRequested += strategy =>
        {
            if (!CanEditSettings())
            {
                _tray.SetState(_settings.ClickThrough, _settings.StartWithWindows, MapLayer(_settings.LayerMode));
                return;
            }
            LayerLifecycleCoordinator.ApplyLayerSelection(
                strategy,
                mode => UpdateSettings(s => s with { LayerMode = mode }),
                selected =>
                {
                    if (_mainWindow is not null)
                    {
                        _mainWindow.SetLayer(selected, LayerRepairTrigger.TraySelection);
                        _backgroundLayer?.Apply(_settings, LayerRepairTrigger.TraySelection);
                    }
                },
                selected => _tray.SetState(
                    _settings.ClickThrough,
                    _settings.StartWithWindows,
                    selected));
        };
        _tray.SettingsRequested += ShowSettings;
        _tray.HighLoadProcessesRequested += ShowHighLoadProcesses;
    }

    private void ApplyDisplayModeFromTray(WidgetDisplayMode requested)
    {
        if (_tray is null)
        {
            return;
        }

        DisplayModeChangeResult result = DisplayModeChangeCoordinator.Apply(
            _settings,
            requested,
            CanEditSettings(),
            _batteryChargeState.PersistSettings,
            settings => _settings = settings,
            settings =>
            {
                _mainWindow?.ApplyVisualSettings(settings, _batteryPresent);
                _backgroundLayer?.Apply(settings, LayerRepairTrigger.SettingsFlowCompleted);
                _windowPlacement?.ApplySettingsLayoutChange();
                _backgroundLayer?.SyncNow();
            },
            _tray.SetDisplayMode);
        if (result == DisplayModeChangeResult.SaveFailed)
        {
            MessageBox.Show(
                "表示幅を保存できなかったため、変更を適用しませんでした。",
                "Desktop System Monitor",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private void ShowHighLoadProcesses()
    {
        if (!_settings.EnableHighLoadProcessDetails || _mainWindow is null)
        {
            return;
        }
        if (_processWindow?.IsVisible == true)
        {
            _processWindow.Activate();
            return;
        }
        _processWindow = new HighLoadProcessesWindow();
        _processWindow.Closed += (_, _) => _processWindow = null;
        _processWindow.SetSuspended(_samplingSuspension.Suspended);
        _processWindow.Show();
    }

    private async void ShowSettings()
    {
        await ShowSettingsAsync().ConfigureAwait(true);
    }

    private async Task ShowSettingsAsync()
    {
        if (_shutdownGate.IsShutdownStarted || _mainWindow is null)
        {
            return;
        }
        if (_settingsStore.IsReadOnly)
        {
            ShowReadOnlySettingsWarning();
            return;
        }
        if (_settingsFlowActive)
        {
            if (_settingsWindow?.IsVisible == true)
            {
                if (_settingsWindow.WindowState == System.Windows.WindowState.Minimized)
                {
                    _settingsWindow.WindowState = System.Windows.WindowState.Normal;
                }
                _settingsWindow.RequestForegroundPresentation(
                    useTopmostPulse: _settings.LayerMode == WindowLayerMode.OnDesktop);
            }
            return;
        }

        _settingsFlowActive = true;
        _tray?.SetEditingEnabled(false);
        bool isOnDesktop = _settings.LayerMode == WindowLayerMode.OnDesktop;
        IDisposable? layerRepairLease = null;
        var completion = new SettingsFlowCompletionScope(
            cleanup: () =>
            {
                _settingsWindow = null;
                _settingsFlowActive = false;
                _tray?.SetEditingEnabled(!_settingsStore.IsReadOnly);
                _tray?.SetState(
                    _settings.ClickThrough,
                    _settings.StartWithWindows,
                    MapLayer(_settings.LayerMode));
                _tray?.SetDisplayMode(_settings.DisplayMode);
            },
            releaseLayerRepair: () => layerRepairLease?.Dispose(),
            reapplyWindowStyles: () =>
            {
                // While a lease is held, releasing it above already resumed and
                // reapplied MainWindow; calling ReapplyWindowStyles again here
                // would issue a second, redundant SetWindowPos(HWND_BOTTOM).
                if (layerRepairLease is null)
                {
                    _mainWindow?.ReapplyWindowStyles(LayerRepairTrigger.SettingsFlowCompleted);
                }
                _backgroundLayer?.Reapply(LayerRepairTrigger.SettingsFlowCompleted);
            },
            onError: ex => _diagnostics.Record("settings-flow-completion-failure", ex));
        try
        {
            OptionalTelemetryCapabilities capabilities = OptionalTelemetryCapabilities.FromSnapshot(_latestSnapshot);
            if (!_settings.ShowBatteryEstimate)
            {
                capabilities = capabilities with
                {
                    Battery = OptionalTelemetryCapabilityStatus.NotDetected,
                };
            }
            SettingsDialogContext context = await CreateSettingsDialogContextAsync(capabilities).ConfigureAwait(true);
            if (_shutdownGate.IsShutdownStarted || _mainWindow is null)
            {
                return;
            }
            var dialog = new SettingsWindow(
                _settings,
                context,
                () => _windowPlacement?.CaptureCurrentWindowPosition(_settings) ?? _settings)
            {
                Owner = _mainWindow,
            };
            _settingsWindow = dialog;
            if (isOnDesktop)
            {
                // Acquired as late as possible (immediately before ShowDialog)
                // so BottomMost repair isn't held off during the async context
                // preparation above, while the dialog isn't visible yet anyway.
                layerRepairLease = _layerRepairSuspension?.Acquire(
                    LayerRepairSuspensionReason.SettingsDialog,
                    LayerRepairTrigger.SettingsFlowCompleted);
            }
            dialog.RequestForegroundPresentation(useTopmostPulse: isOnDesktop);
            if (dialog.ShowDialog() != true || dialog.Result is not SettingsDraft edited)
            {
                return;
            }
            _settingsWindow = null;

            AppSettings previousSettings = _settings;
            AppSettings requested = SettingsEditMerge.Merge(
                _settings,
                edited,
                dialog.ResetBatteryChargeLearningRequested);
            if (requested.StartWithWindows != _settings.StartWithWindows)
            {
                try
                {
                    if (requested.StartWithWindows)
                    {
                        _startupRegistry.Enable(GetExecutablePath());
                    }
                    else
                    {
                        _startupRegistry.Disable();
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"自動起動設定を更新できませんでした。\n{ex.Message}", "Desktop System Monitor", MessageBoxButton.OK, MessageBoxImage.Warning);
                    requested = requested with { StartWithWindows = _settings.StartWithWindows };
                }
            }

            bool intervalChanged = requested.SamplingIntervalSeconds != _settings.SamplingIntervalSeconds;
            _settings = requested.Normalized();
            _batteryChargeState.PersistSettings(_settings);
            bool batteryTargetChanged =
                previousSettings.EffectiveBatteryChargeTargetPercent != _settings.EffectiveBatteryChargeTargetPercent;
            bool batteryVisibilityChanged = previousSettings.ShowBatteryEstimate != _settings.ShowBatteryEstimate;
            if (batteryVisibilityChanged)
            {
                _viewModel.ResetTransientState();
                _batteryChargeState.OnTransientReset();
            }
            else if (batteryTargetChanged)
            {
                _viewModel.ResetBatteryChargeEstimate();
            }
            _diagnostics.Enabled = _settings.DiagnosticLoggingEnabled;
            _network?.SetSelectedAdapters(_settings.SelectedNetworkAdapterLuids);
            _gpu?.SetPreferredAdapter(ParseGpuLuid(_settings.PinnedGpuLuidHex));
            _disk?.SetEnabled(_settings.ShowDiskMetrics, _settings.SelectedPhysicalDiskNumber);
            _battery?.SetEnabled(_settings.ShowBatteryEstimate);
            _mainWindow.Layer = MapLayer(_settings.LayerMode);
            _mainWindow.ClickThrough = _settings.ClickThrough;
            if (!_settings.EnableHighLoadProcessDetails)
            {
                _processWindow?.Close();
            }
            _tray?.SetHighLoadProcessMenuEnabled(_settings.EnableHighLoadProcessDetails);
            _viewModel.ApplyNetworkPeakWindow(_settings.NetworkPeakWindowSeconds);
            _viewModel.ApplyMetricVisibility(_settings.ShowCpuMetrics, _settings.ShowGpuMetrics);
            _mainWindow.ApplyVisualSettings(_settings, _batteryPresent);
            _backgroundLayer?.Apply(_settings, LayerRepairTrigger.SettingsFlowCompleted);
            _windowPlacement?.ApplySettingsLayoutChange();
            _backgroundLayer?.SyncNow();
            _tray?.SetState(_settings.ClickThrough, _settings.StartWithWindows, MapLayer(_settings.LayerMode));
            _tray?.SetDisplayMode(_settings.DisplayMode);
            if (intervalChanged)
            {
                await RestartSamplerAsync().ConfigureAwait(true);
            }
            CheckFullScreen();
        }
        catch (Exception ex)
        {
            _diagnostics.Record("settings-flow-failure", ex);
            MessageBox.Show($"設定を適用できませんでした。\n{ex.Message}", "Desktop System Monitor", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            completion.Dispose();
        }
    }

    private ValueTask<MetricSnapshot> SampleAllAsync(CancellationToken token) =>
        _snapshotSampler!.SampleAsync(token);

    private async Task RestartSamplerAsync()
    {
        if (_sampler is not null)
        {
            await _sampler.DisposeAsync().ConfigureAwait(true);
        }
        _sampler = null;
        _snapshotSampler?.RequestBaselineReset();
        _viewModel.ResetTransientState();
        _batteryChargeState.OnTransientReset();
        StartSampler();
        if (_samplingSuspension.Suspended)
        {
            _sampler?.Pause();
        }
    }

    private void ReconcileStartupSetting()
    {
        if (!_settings.StartWithWindows)
        {
            return;
        }
        bool registered;
        try
        {
            registered = _startupRegistry.IsEnabled(GetExecutablePath());
        }
        catch
        {
            registered = false;
        }
        if (!registered)
        {
            UpdateSettings(s => s with { StartWithWindows = false });
        }
    }

    private void OnSessionSwitch(object sender, SessionSwitchEventArgs e)
    {
        Application.Current?.Dispatcher.BeginInvoke(() =>
        {
            LayerLifecycleCoordinator.HandleSessionSwitch(
                e.Reason,
                locked => _sessionLocked = locked,
                UpdateSamplingSuspension,
                suspended => _layerRepairSuspension?.SetReason(
                    LayerRepairSuspensionReason.SessionLocked,
                    suspended,
                    LayerRepairTrigger.SessionUnlock),
                () => _backgroundLayer?.Reapply(LayerRepairTrigger.SessionUnlock));
        });
    }

    private void CheckFullScreen()
    {
        if (_mainWindow is null)
        {
            return;
        }
        FullScreenVisibilityTransition transition = _fullScreenAutoHide.Evaluate(
            _settings,
            _isForegroundFullScreen(_mainWindow.Handle));
        if (transition == FullScreenVisibilityTransition.None)
        {
            return;
        }
        bool shouldHide = transition == FullScreenVisibilityTransition.EnterHidden;
        if (_backgroundLayer is not null)
        {
            _backgroundLayer.SetWidgetVisible(!shouldHide);
            if (!shouldHide)
            {
                _mainWindow.ReapplyWindowStyles();
                _backgroundLayer.Reapply(LayerRepairTrigger.VisibilityChanged);
            }
        }
        else if (shouldHide)
        {
            _mainWindow.Hide();
        }
        else
        {
            _mainWindow.Show();
            _mainWindow.ReapplyWindowStyles();
        }
        if (_diagnostics.Enabled)
        {
            _diagnostics.Record(shouldHide
                ? "fullscreen-auto-hide-enter"
                : "fullscreen-auto-hide-exit");
        }
        UpdateSamplingSuspension();
    }

    private void UpdateSamplingSuspension()
    {
        SamplingSuspensionTransition transition = _samplingSuspension.Evaluate(
            _sessionLocked,
            _fullScreenAutoHide.Hidden);
        if (transition == SamplingSuspensionTransition.None || _sampler is null)
        {
            return;
        }
        bool suspend = transition == SamplingSuspensionTransition.Pause;
        _processWindow?.SetSuspended(suspend);
        if (suspend)
        {
            _viewModel.ResetTransientState();
            _batteryChargeState.OnTransientReset();
            _power?.PausePolling();
            _sampler.Pause();
        }
        else
        {
            _snapshotSampler?.RequestBaselineReset();
            _power?.ResumePolling();
            _sampler.Resume();
        }
        if (_diagnostics.Enabled)
        {
            _diagnostics.Record(suspend ? "sampling-suspend-enter" : "sampling-suspend-exit");
        }
    }

    private void ListenForActivation()
    {
        if (_activationEvent is null)
        {
            return;
        }
        WaitHandle[] handles = [_activationEvent, _activationCts.Token.WaitHandle];
        while (WaitHandle.WaitAny(handles) == 0)
        {
            Dispatcher? dispatcher = Application.Current?.Dispatcher;
            if (dispatcher is null)
            {
                continue;
            }
            if (!_shutdownGate.TryRunBeforeShutdown(() =>
                {
                    if (!dispatcher.HasShutdownStarted && !dispatcher.HasShutdownFinished)
                    {
                        dispatcher.BeginInvoke(ShowSettings);
                    }
                }))
            {
                return;
            }
        }
    }

    private void PublishSnapshot(MetricSnapshot snapshot)
    {
        Application.Current?.Dispatcher.BeginInvoke(() =>
        {
            _latestSnapshot = snapshot;
            bool present = snapshot.Battery.BatteryPresent && snapshot.Battery.PowerState != BatteryPowerState.Absent;
            if (present == _batteryPresent)
            {
                _pendingBatteryPresent = _batteryPresent;
                _batteryReflowTimer?.Stop();
            }
            else if (_batteryReflowTimer?.IsEnabled != true || present != _pendingBatteryPresent)
            {
                _pendingBatteryPresent = present;
                _batteryReflowTimer?.Stop();
                _batteryReflowTimer?.Start();
            }
            if (_settings.ShowBatteryEstimate)
            {
                _batteryChargeState.ProcessSnapshot(
                    snapshot,
                    _settings,
                    settings => _settings = settings,
                    settings => _viewModel.Apply(snapshot, settings.NetworkUnitSystem, settings));
            }
            else
            {
                _viewModel.Apply(snapshot, _settings.NetworkUnitSystem, _settings);
            }
        });
    }

    private void ApplyPendingBatteryCapability()
    {
        _batteryReflowTimer?.Stop();
        if (_pendingBatteryPresent == _batteryPresent)
        {
            return;
        }
        _batteryPresent = _pendingBatteryPresent;
        if (_mainWindow is not null)
        {
            _mainWindow.ApplyVisualSettings(_settings, _batteryPresent);
            _windowPlacement?.PositionWindow();
            _backgroundLayer?.Apply(_settings, LayerRepairTrigger.DisplayChanged);
            _backgroundLayer?.SyncNow();
        }
    }

    private async Task<SettingsDialogContext> CreateSettingsDialogContextAsync(OptionalTelemetryCapabilities capabilities)
    {
        ImmutableArray<int> availableDisks;
        bool enumerationFailed = false;
        try
        {
            availableDisks = await Task.Run(
                () => DiskMetricSource.EnumerateAvailableDiskNumbers().ToImmutableArray()).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            _diagnostics.Record("settings-disk-enumeration-failure", ex);
            availableDisks = ImmutableArray<int>.Empty;
            enumerationFailed = true;
        }

        return SettingsDialogContext.Create(
            capabilities,
            _settings,
            _latestSnapshot?.Disk,
            availableDisks,
            enumerationFailed);
    }

    private void UpdateSettings(Func<AppSettings, AppSettings> mutator)
    {
        _settings = mutator(_settings).Normalized();
        _batteryChargeState.PersistSettings(_settings);
    }

    private void RequestExit()
    {
        Application.Current?.Dispatcher.BeginInvoke(() =>
        {
            Application.Current.Shutdown();
        });
    }

    public void RecordUnhandled(Exception exception) => _diagnostics.Record("dispatcher-unhandled", exception);

    private bool CanEditSettings()
    {
        if (_settingsFlowActive)
        {
            return false;
        }
        if (!_settingsStore.IsReadOnly)
        {
            return true;
        }
        ShowReadOnlySettingsWarning();
        return false;
    }

    private static void ShowReadOnlySettingsWarning()
    {
        MessageBox.Show(
            "設定ファイルは新しいバージョンのアプリで作成されているため、このバージョンからは変更できません。新しいバージョンを使用してください。",
            "Desktop System Monitor",
            MessageBoxButton.OK,
            MessageBoxImage.Warning);
    }

    private void OnLayerFallback()
    {
        LayerLifecycleCoordinator.HandleLayerFallback(
            setRuntimeNormal: () =>
                _settings = (_settings with { LayerMode = WindowLayerMode.Normal }).Normalized(),
            saveSettings: () => _settingsStore.Save(_settings),
            updateTray: () =>
                _tray?.SetState(
                    _settings.ClickThrough,
                    _settings.StartWithWindows,
                    LayerStrategy.Normal),
            recordDiagnostic: _diagnostics.Record);
        _backgroundLayer?.Apply(_settings, LayerRepairTrigger.ExplicitReapply);
    }

    private static LayerStrategy MapLayer(WindowLayerMode mode) => mode switch
    {
        WindowLayerMode.AlwaysOnTop => LayerStrategy.TopMost,
        WindowLayerMode.Normal => LayerStrategy.Normal,
        _ => LayerStrategy.BottomMost,
    };

    internal static string GetExecutablePath()
    {
        string? path = Environment.ProcessPath;
        return !string.IsNullOrEmpty(path)
            ? path
            : GetFallbackExecutablePath();
    }

    internal static string GetFallbackExecutablePath()
    {
        return Path.Combine(AppContext.BaseDirectory, "DesktopSystemMonitor.exe");
    }

    private static ulong? ParseGpuLuid(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }
        ReadOnlySpan<char> token = value.AsSpan().Trim();
        if (token.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            token = token[2..];
        }
        return ulong.TryParse(token, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out ulong luid)
            ? luid
            : null;
    }

    private IMetricSource<T> CreateSource<T>(Func<IMetricSource<T>> factory, Func<T> unavailable)
    {
        try
        {
            return factory();
        }
        catch (Exception ex)
        {
            _diagnostics.Record($"{typeof(T).Name}-source-unavailable", ex);
            return new UnavailableMetricSource<T>(unavailable);
        }
    }

    private IGpuMetricSource CreateGpuSource()
    {
        try
        {
            return new GpuMetricSource();
        }
        catch (Exception ex)
        {
            _diagnostics.Record("GpuSnapshot-source-unavailable", ex);
            return new UnavailableGpuMetricSource();
        }
    }

    private INetworkMetricSource CreateNetworkSource()
    {
        try
        {
            return new NetworkMetricSource();
        }
        catch (Exception ex)
        {
            _diagnostics.Record("NetworkSnapshot-source-unavailable", ex);
            return new UnavailableNetworkMetricSource();
        }
    }

    private IPowerMetricSource CreatePowerSource()
    {
        try
        {
            return new HardwarePowerMetricSource(
                (stage, exception) => _diagnostics.Record(stage, exception));
        }
        catch (Exception ex)
        {
            _diagnostics.Record("PowerSnapshot-source-unavailable", ex);
            return new UnavailablePowerMetricSource();
        }
    }

    private IDiskMetricSource CreateDiskSource()
    {
        try { return new DiskMetricSource(); }
        catch (Exception ex)
        {
            _diagnostics.Record("DiskSnapshot-source-unavailable", ex);
            return new UnavailableDiskMetricSource();
        }
    }

    private IBatteryMetricSource CreateBatterySource()
    {
        try { return new BatteryMetricSource(); }
        catch (Exception ex)
        {
            _diagnostics.Record("BatterySnapshot-source-unavailable", ex);
            return new UnavailableBatteryMetricSource();
        }
    }

    private sealed class UnavailableMetricSource<T>(Func<T> factory) : IMetricSource<T>
    {
        public ValueTask<T> SampleAsync(CancellationToken cancellationToken) => ValueTask.FromResult(factory());
        public void ResetBaseline() { }
        public void Dispose() { }
    }

    private sealed class UnavailableGpuMetricSource : IGpuMetricSource
    {
        public ValueTask<GpuSnapshot> SampleAsync(CancellationToken cancellationToken) => ValueTask.FromResult(GpuSnapshot.Unavailable());
        public void SetPreferredAdapter(ulong? luid) { }
        public void ResetBaseline() { }
        public void Dispose() { }
    }

    private sealed class UnavailableNetworkMetricSource : INetworkMetricSource
    {
        public ValueTask<NetworkSnapshot> SampleAsync(CancellationToken cancellationToken) => ValueTask.FromResult(NetworkSnapshot.Unavailable());
        public void SetSelectedAdapters(IEnumerable<ulong> luids) { }
        public void ResetBaseline() { }
        public void Dispose() { }
    }

    private sealed class UnavailablePowerMetricSource : IPowerMetricSource
    {
        public ValueTask<PowerSnapshot> SampleAsync(CancellationToken cancellationToken) =>
            ValueTask.FromResult(PowerSnapshot.Unavailable());

        public void PausePolling() { }
        public void ResumePolling() { }
        public void ResetBaseline() { }
        public void Dispose() { }
    }

    private sealed class UnavailableDiskMetricSource : IDiskMetricSource
    {
        public ValueTask<DiskSnapshot> SampleAsync(CancellationToken cancellationToken) => ValueTask.FromResult(DiskSnapshot.Unavailable());
        public void SetEnabled(bool enabled, int? selectedDiskNumber) { }
        public void ResetBaseline() { }
        public void Dispose() { }
    }

    private sealed class UnavailableBatteryMetricSource : IBatteryMetricSource
    {
        public ValueTask<BatterySnapshot> SampleAsync(CancellationToken cancellationToken) => ValueTask.FromResult(BatterySnapshot.Unavailable());
        public void SetEnabled(bool enabled) { }
        public void ResetBaseline() { }
        public void Dispose() { }
    }

    public void Dispose()
    {
        _shutdownGate.BeginShutdown();
        SystemEvents.SessionSwitch -= OnSessionSwitch;
        _fullscreenTimer?.Stop();
        _batteryReflowTimer?.Stop();
        // The shutdown gate prevents FlushPendingWindowPosition from starting a
        // new reflow. Flush a stable user position before the coordinator starts
        // rejecting all persistence; a pending reflow remains suppressed.
        _windowPlacement?.FlushBeforeShutdown();
        if (_mainWindow is not null)
        {
            _mainWindow.LayerDiagnosticOccurred -= _diagnostics.RecordLayerEvent;
            _mainWindow.LayerFallbackOccurred -= OnLayerFallback;
        }
        _windowPlacement?.Dispose();
        _backgroundLayer?.Dispose();
        _activationCts.Cancel();
        try
        {
            _activationListener?.GetAwaiter().GetResult();
        }
        catch (OperationCanceledException)
        {
            // expected
        }
        _sampler?.DisposeAsync().AsTask().GetAwaiter().GetResult();
        _tray?.Dispose();
        _processWindow?.Close();
        _cpu?.Dispose();
        _memory?.Dispose();
        _gpu?.Dispose();
        _network?.Dispose();
        _power?.Dispose();
        if (_temperature is not null && !ReferenceEquals(_temperature, _power)) _temperature.Dispose();
        _disk?.Dispose();
        _battery?.Dispose();
        _activationEvent?.Dispose();
        _activationCts.Dispose();
        if (_mutex is not null)
        {
            try
            {
                if (_mutexOwned)
                {
                    _mutex.ReleaseMutex();
                }
            }
            catch
            {
                // ignore
            }
            _mutex.Dispose();
        }
        _diagnostics.Dispose();
    }
}
