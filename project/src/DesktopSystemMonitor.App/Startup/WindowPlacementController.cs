using System.ComponentModel;
using System.Windows.Threading;
using DesktopSystemMonitor.Core.Layout;
using DesktopSystemMonitor.Core.Settings;
using DesktopSystemMonitor.Windows.Window;
using Rect = DesktopSystemMonitor.Core.Layout.Rect;

namespace DesktopSystemMonitor.App.Startup;

internal interface IWindowPlacementSurface
{
    double Left { get; set; }
    double Top { get; set; }
    double Width { get; }
    double Height { get; }
    bool IsLoaded { get; }
    event Action? LocationChanged;
    event Action? Closing;
    event Action? DisplayConfigurationChanged;
    event Action? UserMoveStarted;
    event Action? UserMoveCompleted;
}

internal interface IPlacementTimer
{
    bool IsEnabled { get; }
    TimeSpan Interval { get; set; }
    void Start();
    void Stop();
}

internal readonly record struct MonitorLayout(IReadOnlyList<MonitorInfo> All, MonitorInfo Primary);

internal sealed class WindowPlacementController : IDisposable
{
    private static readonly TimeSpan DisplayReflowDebounce = TimeSpan.FromMilliseconds(750);
    private static readonly TimeSpan DisplayReflowMaximumDelay = TimeSpan.FromSeconds(2);
    private readonly IWindowPlacementSurface _window;
    private readonly Func<AppSettings> _getSettings;
    private readonly Action<Func<AppSettings, AppSettings>> _updateSettings;
    private readonly Func<MonitorLayout> _enumerateMonitors;
    private readonly Func<DateTimeOffset> _utcNow;
    private readonly Action<string, Exception?> _recordDiagnostic;
    private readonly IPlacementTimer _positionPersistTimer;
    private readonly IPlacementTimer _displayReflowTimer;
    private readonly MainWindowPlacementSurface? _surfaceLifetime;
    private readonly DisplayReflowCoordinator _displayReflow = new(
        DisplayReflowDebounce,
        DisplayReflowMaximumDelay);
    private DisplayLayoutSnapshot? _displayLayoutSnapshot;
    private WindowPlacementIntent? _placementIntent;
    private bool _applyingAutomaticPosition;
    private bool _disposed;

    internal WindowPlacementController(
        MainWindow window,
        Func<AppSettings> getSettings,
        Action<Func<AppSettings, AppSettings>> updateSettings,
        Action<string, Exception?> recordDiagnostic,
        Dispatcher dispatcher)
    {
        var surface = new MainWindowPlacementSurface(window);
        _window = surface;
        _surfaceLifetime = surface;
        _getSettings = getSettings;
        _updateSettings = updateSettings;
        _recordDiagnostic = recordDiagnostic;
        _enumerateMonitors = EnumerateMonitors;
        _utcNow = () => DateTimeOffset.UtcNow;
        _positionPersistTimer = new DispatcherPlacementTimer(
            TimeSpan.FromMilliseconds(300),
            dispatcher,
            FlushPendingWindowPosition);
        _displayReflowTimer = new DispatcherPlacementTimer(
            DisplayReflowDebounce,
            dispatcher,
            CompletePendingDisplayReflow);
        HookEvents();
    }

    internal WindowPlacementController(
        IWindowPlacementSurface window,
        Func<AppSettings> getSettings,
        Action<Func<AppSettings, AppSettings>> updateSettings,
        Func<MonitorLayout> enumerateMonitors,
        Func<DateTimeOffset> utcNow,
        Action<string, Exception?> recordDiagnostic,
        IPlacementTimer positionPersistTimer,
        IPlacementTimer displayReflowTimer)
    {
        _window = window;
        _getSettings = getSettings;
        _updateSettings = updateSettings;
        _enumerateMonitors = enumerateMonitors;
        _utcNow = utcNow;
        _recordDiagnostic = recordDiagnostic;
        _positionPersistTimer = positionPersistTimer;
        _displayReflowTimer = displayReflowTimer;
        HookEvents();
    }

    internal bool ReflowPending => _displayReflow.Pending;

    internal void PositionWindow()
    {
        MonitorLayout layout = _enumerateMonitors();
        AppSettings settings = _getSettings();
        Rect rect = WindowAnchor.Compute(
            _window.Width,
            _window.Height,
            layout.All,
            layout.Primary,
            settings.SavedMonitorDeviceName,
            settings.SavedRightEdgeDip,
            settings.SavedTopEdgeDip,
            settings.PlacementMode,
            settings.PlacementAnchor,
            settings.HorizontalMarginDip,
            settings.VerticalMarginDip,
            settings.SavedWorkAreaWidthDip,
            settings.SavedWorkAreaHeightDip,
            settings.PlacementXRatio,
            settings.PlacementYRatio);
        ApplyWindowRect(rect);
        UpdateDisplaySnapshot(layout.All, layout.Primary, rect);
        CapturePlacementIntent(rect, layout.All, layout.Primary);
    }

    internal void ApplySettingsLayoutChange()
    {
        if (ReflowPending)
        {
            CompletePendingDisplayReflow(persistPosition: false);
            if (ReflowPending)
            {
                return;
            }
        }
        PositionWindow();
        PersistWindowPosition(markCustom: false);
    }

    internal void FlushBeforeShutdown()
    {
        FlushPendingWindowPosition();
        _displayReflow.BeginShutdown();
        _positionPersistTimer.Stop();
        _displayReflowTimer.Stop();
    }

    private void HookEvents()
    {
        _window.LocationChanged += OnWindowLocationChanged;
        _window.Closing += OnWindowClosing;
        _window.DisplayConfigurationChanged += BeginDisplayReflow;
        _window.UserMoveStarted += OnUserMoveStarted;
        _window.UserMoveCompleted += OnUserMoveCompleted;
    }

    internal void PersistWindowPosition(
        IReadOnlyList<MonitorInfo>? knownMonitors = null,
        MonitorInfo? knownPrimary = null,
        bool markCustom = true)
    {
        if (!_window.IsLoaded)
        {
            return;
        }
        MonitorLayout layout = knownMonitors is null || knownPrimary is null
            ? _enumerateMonitors()
            : new MonitorLayout(knownMonitors, knownPrimary);
        Rect widget = CurrentWindowRect();
        MonitorInfo monitor = WindowAnchor.FindMonitorForWidget(widget, layout.All, layout.Primary);
        bool captured = WindowAnchor.TryCaptureIntent(widget, monitor, out WindowPlacementIntent intent);
        UpdateDisplaySnapshot(layout.All, layout.Primary, widget);
        _updateSettings(settings => !markCustom && settings.PlacementMode == WindowPlacementMode.Preset
            ? settings
            : settings with
            {
                SavedMonitorDeviceName = monitor.DeviceName,
                SavedRightEdgeDip = widget.Right,
                SavedTopEdgeDip = widget.Top,
                SavedMonitorDpi = monitor.Dpi,
                PlacementXRatio = captured ? intent.XRatio : null,
                PlacementYRatio = captured ? intent.YRatio : null,
                SavedWorkAreaWidthDip = captured ? monitor.WorkArea.Width : null,
                SavedWorkAreaHeightDip = captured ? monitor.WorkArea.Height : null,
                PlacementMode = markCustom ? WindowPlacementMode.Custom : settings.PlacementMode,
            });
    }

    internal AppSettings CaptureCurrentWindowPosition(AppSettings basis)
    {
        if (!_window.IsLoaded)
        {
            return basis;
        }
        MonitorLayout layout = _enumerateMonitors();
        Rect widget = CurrentWindowRect();
        MonitorInfo monitor = WindowAnchor.FindMonitorForWidget(widget, layout.All, layout.Primary);
        bool captured = WindowAnchor.TryCaptureIntent(widget, monitor, out WindowPlacementIntent intent);
        return basis with
        {
            SavedMonitorDeviceName = monitor.DeviceName,
            SavedRightEdgeDip = widget.Right,
            SavedTopEdgeDip = widget.Top,
            SavedMonitorDpi = monitor.Dpi,
            PlacementXRatio = captured ? intent.XRatio : null,
            PlacementYRatio = captured ? intent.YRatio : null,
            SavedWorkAreaWidthDip = captured ? monitor.WorkArea.Width : null,
            SavedWorkAreaHeightDip = captured ? monitor.WorkArea.Height : null,
            PlacementMode = WindowPlacementMode.Custom,
        };
    }

    internal void FlushPendingWindowPosition()
    {
        if (!_positionPersistTimer.IsEnabled)
        {
            return;
        }
        _positionPersistTimer.Stop();
        MonitorLayout current = _enumerateMonitors();
        bool layoutChanged = _displayLayoutSnapshot is not null
            && !LayoutsEquivalent(_displayLayoutSnapshot.Monitors, current.All);
        if (!_displayReflow.CanPersist(layoutChanged))
        {
            if (layoutChanged)
            {
                BeginDisplayReflow();
            }
            return;
        }
        PersistWindowPosition(current.All, current.Primary);
    }

    private void OnWindowLocationChanged()
    {
        if (_applyingAutomaticPosition)
        {
            return;
        }
        if (_displayLayoutSnapshot is not null)
        {
            _displayLayoutSnapshot = _displayLayoutSnapshot with { WidgetRect = CurrentWindowRect() };
        }
        if (_displayReflow.Pending)
        {
            return;
        }
        _positionPersistTimer.Stop();
        _positionPersistTimer.Start();
    }

    private void OnWindowClosing() => FlushPendingWindowPosition();

    private void OnUserMoveStarted()
    {
        _displayReflow.BeginUserMove();
        _positionPersistTimer.Stop();
    }

    private void OnUserMoveCompleted()
    {
        _displayReflow.EndUserMove();
        if (!_window.IsLoaded)
        {
            return;
        }
        MonitorLayout current = _enumerateMonitors();
        Rect widget = CurrentWindowRect();
        UpdateDisplaySnapshot(current.All, current.Primary, widget);
        CapturePlacementIntent(widget, current.All, current.Primary);
        if (!_displayReflow.Pending)
        {
            _positionPersistTimer.Stop();
            _positionPersistTimer.Start();
        }
    }

    private void BeginDisplayReflow()
    {
        if (_disposed)
        {
            return;
        }
        _positionPersistTimer.Stop();
        TimeSpan? delay = _displayReflow.BeginWave(_utcNow());
        if (delay is null)
        {
            return;
        }
        _displayReflowTimer.Stop();
        _displayReflowTimer.Interval = delay.Value;
        _displayReflowTimer.Start();
    }

    internal void CompletePendingDisplayReflow() => CompletePendingDisplayReflow(persistPosition: true);

    private void CompletePendingDisplayReflow(bool persistPosition)
    {
        _displayReflowTimer.Stop();
        DisplayReflowCompletionDecision decision = _displayReflow.RequestCompletion();
        if (decision == DisplayReflowCompletionDecision.Ignore || _disposed)
        {
            return;
        }
        if (decision == DisplayReflowCompletionDecision.RetryAfterUserMove)
        {
            _displayReflowTimer.Interval = TimeSpan.FromMilliseconds(100);
            _displayReflowTimer.Start();
            return;
        }

        try
        {
            MonitorLayout current = _enumerateMonitors();
            Rect rect;
            AppSettings settings = _getSettings();
            WindowPlacementIntent? placementIntent = settings.PlacementMode == WindowPlacementMode.Preset
                ? null
                : ResolvePlacementIntentFromSnapshot();
            if (placementIntent is WindowPlacementIntent intent
                && WindowAnchor.TryComputeFromIntent(
                    _window.Width,
                    _window.Height,
                    current.All,
                    current.Primary,
                    intent,
                    out Rect reflowed))
            {
                rect = reflowed;
            }
            else
            {
                rect = WindowAnchor.Compute(
                    _window.Width,
                    _window.Height,
                    current.All,
                    current.Primary,
                    settings.SavedMonitorDeviceName,
                    settings.SavedRightEdgeDip,
                    settings.SavedTopEdgeDip,
                    settings.PlacementMode,
                    settings.PlacementAnchor,
                    settings.HorizontalMarginDip,
                    settings.VerticalMarginDip,
                    settings.SavedWorkAreaWidthDip,
                    settings.SavedWorkAreaHeightDip,
                    settings.PlacementXRatio,
                    settings.PlacementYRatio);
            }
            ApplyWindowRect(rect);
            UpdateDisplaySnapshot(current.All, current.Primary, rect);
            if (persistPosition)
            {
                PersistWindowPosition(current.All, current.Primary, markCustom: false);
            }
        }
        catch (Exception ex)
        {
            _recordDiagnostic("display-reflow-failure", ex);
        }
        finally
        {
            _displayReflow.FinishCompletion();
        }
    }

    private WindowPlacementIntent? ResolvePlacementIntentFromSnapshot()
    {
        if (_placementIntent is WindowPlacementIntent currentIntent)
        {
            return currentIntent;
        }
        if (_displayLayoutSnapshot is null)
        {
            return null;
        }
        MonitorInfo monitor = WindowAnchor.FindMonitorForWidget(
            _displayLayoutSnapshot.WidgetRect,
            _displayLayoutSnapshot.Monitors,
            _displayLayoutSnapshot.Primary);
        return WindowAnchor.TryCaptureIntent(
            _displayLayoutSnapshot.WidgetRect,
            monitor,
            out WindowPlacementIntent captured)
            ? captured
            : null;
    }

    private void CapturePlacementIntent(
        Rect widget,
        IReadOnlyList<MonitorInfo> monitors,
        MonitorInfo primary)
    {
        MonitorInfo monitor = WindowAnchor.FindMonitorForWidget(widget, monitors, primary);
        if (WindowAnchor.TryCaptureIntent(widget, monitor, out WindowPlacementIntent intent))
        {
            _placementIntent = intent;
        }
    }

    private void UpdateDisplaySnapshot(
        IReadOnlyList<MonitorInfo> monitors,
        MonitorInfo primary,
        Rect widget)
    {
        _displayLayoutSnapshot = new DisplayLayoutSnapshot(monitors.ToArray(), primary, widget);
    }

    private void ApplyWindowRect(Rect rect)
    {
        try
        {
            _applyingAutomaticPosition = true;
            _window.Left = rect.Left;
            _window.Top = rect.Top;
        }
        finally
        {
            _applyingAutomaticPosition = false;
        }
    }

    private Rect CurrentWindowRect() => new(_window.Left, _window.Top, _window.Width, _window.Height);

    internal static bool LayoutsEquivalent(MonitorInfo[] left, IReadOnlyList<MonitorInfo> right)
    {
        if (left.Length != right.Count)
        {
            return false;
        }
        MonitorInfo[] orderedLeft = left.OrderBy(item => item.DeviceName, StringComparer.OrdinalIgnoreCase).ToArray();
        MonitorInfo[] orderedRight = right.OrderBy(item => item.DeviceName, StringComparer.OrdinalIgnoreCase).ToArray();
        for (int index = 0; index < orderedLeft.Length; index++)
        {
            if (!string.Equals(orderedLeft[index].DeviceName, orderedRight[index].DeviceName, StringComparison.OrdinalIgnoreCase)
                || orderedLeft[index].WorkArea != orderedRight[index].WorkArea
                || orderedLeft[index].Dpi != orderedRight[index].Dpi)
            {
                return false;
            }
        }
        return true;
    }

    private static MonitorLayout EnumerateMonitors()
    {
        var (all, primary) = MonitorEnumerator.Enumerate();
        return new MonitorLayout(all, primary);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;
        _positionPersistTimer.Stop();
        _displayReflowTimer.Stop();
        _window.LocationChanged -= OnWindowLocationChanged;
        _window.Closing -= OnWindowClosing;
        _window.DisplayConfigurationChanged -= BeginDisplayReflow;
        _window.UserMoveStarted -= OnUserMoveStarted;
        _window.UserMoveCompleted -= OnUserMoveCompleted;
        _surfaceLifetime?.Dispose();
    }

    private sealed record DisplayLayoutSnapshot(MonitorInfo[] Monitors, MonitorInfo Primary, Rect WidgetRect);
}

internal sealed class MainWindowPlacementSurface : IWindowPlacementSurface, IDisposable
{
    private readonly MainWindow _window;

    internal MainWindowPlacementSurface(MainWindow window)
    {
        _window = window;
        _window.LocationChanged += HandleLocationChanged;
        _window.Closing += HandleClosing;
        _window.DisplayConfigurationChanged += HandleDisplayConfigurationChanged;
        _window.UserMoveStarted += HandleUserMoveStarted;
        _window.UserMoveCompleted += HandleUserMoveCompleted;
    }

    public double Left { get => _window.Left; set => _window.Left = value; }
    public double Top { get => _window.Top; set => _window.Top = value; }
    public double Width => _window.Width;
    public double Height => _window.Height;
    public bool IsLoaded => _window.IsLoaded;
    public event Action? LocationChanged;
    public event Action? Closing;
    public event Action? DisplayConfigurationChanged;
    public event Action? UserMoveStarted;
    public event Action? UserMoveCompleted;

    private void HandleLocationChanged(object? sender, EventArgs e) => LocationChanged?.Invoke();
    private void HandleClosing(object? sender, CancelEventArgs e) => Closing?.Invoke();
    private void HandleDisplayConfigurationChanged() => DisplayConfigurationChanged?.Invoke();
    private void HandleUserMoveStarted() => UserMoveStarted?.Invoke();
    private void HandleUserMoveCompleted() => UserMoveCompleted?.Invoke();

    public void Dispose()
    {
        _window.LocationChanged -= HandleLocationChanged;
        _window.Closing -= HandleClosing;
        _window.DisplayConfigurationChanged -= HandleDisplayConfigurationChanged;
        _window.UserMoveStarted -= HandleUserMoveStarted;
        _window.UserMoveCompleted -= HandleUserMoveCompleted;
    }
}

internal sealed class DispatcherPlacementTimer : IPlacementTimer
{
    private readonly DispatcherTimer _timer;

    internal DispatcherPlacementTimer(TimeSpan interval, Dispatcher dispatcher, Action callback)
    {
        _timer = new DispatcherTimer(interval, DispatcherPriority.Background, (_, _) => callback(), dispatcher);
        _timer.Stop();
    }

    public bool IsEnabled => _timer.IsEnabled;
    public TimeSpan Interval { get => _timer.Interval; set => _timer.Interval = value; }
    public void Start() => _timer.Start();
    public void Stop() => _timer.Stop();
}
