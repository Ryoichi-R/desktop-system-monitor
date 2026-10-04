using System.ComponentModel;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using DesktopSystemMonitor.Core.Layout;
using DesktopSystemMonitor.Core.Settings;
using DesktopSystemMonitor.Windows.Window;
using Rect = DesktopSystemMonitor.Core.Layout.Rect;

namespace DesktopSystemMonitor.App.Startup;

internal interface IWindowPlacementSurface
{
    Rect Bounds { get; }
    double? CurrentDpi { get; }
    bool IsLoaded { get; }
    string DiagnosticDescription => $"PhysicalBounds={Bounds}; windowDpi={CurrentDpi}";
    bool TrySetPosition(double left, double top);
    event Action? LocationChanged;
    event Action? Closing;
    event Action? DisplayConfigurationChanged;
    event Action? BoundsChanged;
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
    private static readonly TimeSpan PositionPersistDebounce = TimeSpan.FromMilliseconds(300);
    private const int MaximumPlacementAttempts = 3;
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
    private PlacementPreviewSession? _previewSession;
    private AppSettings? _previewSettings;
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
            PositionPersistDebounce,
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
    internal bool PlacementPreviewActive => _previewSession is not null;

    internal bool PositionWindow() => PositionWindow(_previewSettings ?? _getSettings());

    private bool PositionWindow(AppSettings settings)
    {
        if (_disposed || !ApplyVerifiedPlacement(
            settings,
            (layout, bounds) => ComputeWindowRect(settings, layout, bounds.Width, bounds.Height),
            out MonitorLayout layout,
            out Rect actual))
        {
            return false;
        }
        UpdateDisplaySnapshot(layout.All, layout.Primary, actual);
        if (_window.IsLoaded && _previewSession is null)
        {
            CapturePlacementIntent(actual, layout.All, layout.Primary);
        }
        return true;
    }

    internal void ApplySettingsLayoutChange()
    {
        if (_previewSession is not null)
        {
            return;
        }
        if (ReflowPending)
        {
            CompletePendingDisplayReflow(persistPosition: false);
            if (ReflowPending)
            {
                return;
            }
        }
        if (PositionWindow())
        {
            PersistWindowPosition(markCustom: false);
        }
    }

    internal void BeginPlacementPreviewSession()
    {
        if (_previewSession is not null)
        {
            return;
        }

        if (ReflowPending)
        {
            CompletePendingDisplayReflow(persistPosition: true);
        }
        FlushPendingWindowPosition();
        MonitorLayout layout = ToPhysicalLayout(_enumerateMonitors());
        Rect current = CurrentWindowRect();
        _previewSession = new PlacementPreviewSession(
            _getSettings(),
            current,
            layout,
            _placementIntent);
        _previewSettings = _getSettings();
        _positionPersistTimer.Stop();
    }

    internal string PreviewPlacement(AppSettings draft)
    {
        ArgumentNullException.ThrowIfNull(draft);
        BeginPlacementPreviewSession();
        _previewSettings = draft.Normalized();
        _positionPersistTimer.Stop();

        bool applied = PositionWindow(_previewSettings);
        MonitorLayout layout = ToPhysicalLayout(_enumerateMonitors());
        MonitorInfo target = ResolveTarget(layout, _previewSettings.SavedMonitorDeviceName);
        Rect actual = CurrentWindowRect();
        bool fallback = _previewSettings.SavedMonitorDeviceName is { Length: > 0 } requested
            && !layout.All.Any(monitor => string.Equals(monitor.DeviceName, requested, StringComparison.OrdinalIgnoreCase));
        bool oversized = actual.Width > target.WorkArea.Width + 1 || actual.Height > target.WorkArea.Height + 1;
        string anchor = AnchorLabel(_previewSettings.PlacementAnchor);
        string result = !applied
            ? "表示位置を適用できませんでした。診断ログを確認してください。"
            : fallback
                ? $"一時表示中: 指定画面は未接続のため、メイン画面の{anchor}に表示しています。"
                : $"一時表示中: {target.DeviceName} の{anchor}に表示しています。";
        if (applied && oversized)
        {
            result += " 表示領域がウィジェットより小さいため、全体は収まりません。";
        }
        return result;
    }

    internal bool CommitPlacementPreview(AppSettings committedSettings, Func<bool>? persistSettings = null)
    {
        ArgumentNullException.ThrowIfNull(committedSettings);
        if (_previewSession is null)
        {
            return persistSettings?.Invoke() ?? true;
        }
        if (ReflowPending)
        {
            CompletePendingDisplayReflow(persistPosition: false);
        }
        _positionPersistTimer.Stop();
        _previewSettings = committedSettings.Normalized();
        if (!PositionWindow(_previewSettings) || persistSettings?.Invoke() == false)
        {
            return false;
        }
        _previewSession = null;
        _previewSettings = null;
        _placementIntent = null;
        return true;
    }

    internal bool CancelPlacementPreviewSession()
    {
        PlacementPreviewSession? session = _previewSession;
        if (session is null)
        {
            return true;
        }
        try
        {
            _positionPersistTimer.Stop();
            bool moved = ApplyVerifiedPlacement(
                session.OriginalSettings,
                (layout, bounds) => ComputeRestoredRect(session, layout, bounds),
                out MonitorLayout currentLayout,
                out Rect actual);
            if (moved)
            {
                UpdateDisplaySnapshot(currentLayout.All, currentLayout.Primary, actual);
            }
            _placementIntent = session.OriginalPlacementIntent;
            return moved;
        }
        catch (Exception ex)
        {
            RecordDiagnostic("placement-preview-cancel-failure", ex);
            return false;
        }
        finally
        {
            _previewSession = null;
            _previewSettings = null;
            _positionPersistTimer.Stop();
            _displayReflowTimer.Stop();
            _displayReflow.FinishCompletion();
        }
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
        _window.BoundsChanged += BeginDisplayReflow;
        _window.UserMoveStarted += OnUserMoveStarted;
        _window.UserMoveCompleted += OnUserMoveCompleted;
    }

    internal void PersistWindowPosition(
        IReadOnlyList<MonitorInfo>? knownMonitors = null,
        MonitorInfo? knownPrimary = null,
        bool markCustom = true)
    {
        if (_previewSession is not null)
        {
            _positionPersistTimer.Stop();
            return;
        }
        if (!_window.IsLoaded)
        {
            return;
        }
        MonitorLayout layout = knownMonitors is null || knownPrimary is null
            ? ToPhysicalLayout(_enumerateMonitors())
            : ToPhysicalLayout(new MonitorLayout(knownMonitors, knownPrimary));
        Rect widget = CurrentWindowRect();
        if (!IsFinitePositive(widget.Width) || !IsFinitePositive(widget.Height))
        {
            RecordDiagnostic("placement-size-not-ready", null);
            return;
        }
        MonitorInfo monitor = WindowAnchor.FindMonitorForWidget(widget, layout.All, layout.Primary);
        bool captured = WindowAnchor.TryCaptureIntent(widget, monitor, out WindowPlacementIntent intent);
        double scale = DpiScale(monitor);
        UpdateDisplaySnapshot(layout.All, layout.Primary, widget);
        _updateSettings(settings => !markCustom && settings.PlacementMode == WindowPlacementMode.Preset
            ? settings
            : settings with
            {
                SavedMonitorDeviceName = monitor.DeviceName,
                SavedRightEdgeDip = widget.Right / scale,
                SavedTopEdgeDip = widget.Top / scale,
                SavedMonitorDpi = monitor.Dpi,
                PlacementXRatio = captured ? intent.XRatio : null,
                PlacementYRatio = captured ? intent.YRatio : null,
                SavedWorkAreaWidthDip = captured ? monitor.WorkArea.Width / scale : null,
                SavedWorkAreaHeightDip = captured ? monitor.WorkArea.Height / scale : null,
                PlacementMode = markCustom ? WindowPlacementMode.Custom : settings.PlacementMode,
            });
    }

    internal AppSettings CaptureCurrentWindowPosition(AppSettings basis)
    {
        if (!_window.IsLoaded)
        {
            return basis;
        }
        MonitorLayout layout = ToPhysicalLayout(_enumerateMonitors());
        Rect widget = CurrentWindowRect();
        MonitorInfo monitor = WindowAnchor.FindMonitorForWidget(widget, layout.All, layout.Primary);
        bool captured = WindowAnchor.TryCaptureIntent(widget, monitor, out WindowPlacementIntent intent);
        double scale = DpiScale(monitor);
        return basis with
        {
            SavedMonitorDeviceName = monitor.DeviceName,
            SavedRightEdgeDip = widget.Right / scale,
            SavedTopEdgeDip = widget.Top / scale,
            SavedMonitorDpi = monitor.Dpi,
            PlacementXRatio = captured ? intent.XRatio : null,
            PlacementYRatio = captured ? intent.YRatio : null,
            SavedWorkAreaWidthDip = captured ? monitor.WorkArea.Width / scale : null,
            SavedWorkAreaHeightDip = captured ? monitor.WorkArea.Height / scale : null,
            PlacementMode = WindowPlacementMode.Custom,
        };
    }

    internal void FlushPendingWindowPosition()
    {
        if (_previewSession is not null)
        {
            _positionPersistTimer.Stop();
            return;
        }
        if (!_positionPersistTimer.IsEnabled)
        {
            return;
        }
        _positionPersistTimer.Stop();
        MonitorLayout current = ToPhysicalLayout(_enumerateMonitors());
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
        if (_applyingAutomaticPosition || _previewSession is not null)
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
        if (_previewSession is not null || !_window.IsLoaded)
        {
            return;
        }
        MonitorLayout current = ToPhysicalLayout(_enumerateMonitors());
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
            AppSettings settings = _previewSettings ?? _getSettings();
            WindowPlacementIntent? placementIntent = _previewSession is null && settings.PlacementMode != WindowPlacementMode.Preset
                ? ResolvePlacementIntentFromSnapshot()
                : null;
            bool moved = ApplyVerifiedPlacement(
                settings,
                (layout, bounds) => placementIntent is WindowPlacementIntent intent
                    && WindowAnchor.TryComputeFromIntent(bounds.Width, bounds.Height, layout.All, layout.Primary, intent, out Rect reflowed)
                        ? reflowed
                        : ComputeWindowRect(settings, layout, bounds.Width, bounds.Height),
                out MonitorLayout current,
                out Rect actual);
            if (!moved)
            {
                return;
            }
            UpdateDisplaySnapshot(current.All, current.Primary, actual);
            if (_previewSession is null && persistPosition)
            {
                PersistWindowPosition(current.All, current.Primary, markCustom: false);
            }
        }
        catch (Exception ex)
        {
            RecordDiagnostic("display-reflow-failure", ex);
        }
        finally
        {
            _displayReflow.FinishCompletion();
        }
    }

    private static Rect ComputeWindowRect(AppSettings settings, MonitorLayout layout, double width, double height)
    {
        MonitorInfo target = ResolveTarget(layout, settings.SavedMonitorDeviceName);
        double targetScale = DpiScale(target);
        double savedScale = settings.SavedMonitorDpi is { } dpi && double.IsFinite(dpi) && dpi > 0
            ? dpi / 96d
            : targetScale;
        return WindowAnchor.Compute(
            width,
            height,
            layout.All,
            layout.Primary,
            settings.SavedMonitorDeviceName,
            ScaleOptional(settings.SavedRightEdgeDip, savedScale),
            ScaleOptional(settings.SavedTopEdgeDip, savedScale),
            settings.PlacementMode,
            settings.PlacementAnchor,
            settings.HorizontalMarginDip * targetScale,
            settings.VerticalMarginDip * targetScale,
            ScaleOptional(settings.SavedWorkAreaWidthDip, savedScale),
            ScaleOptional(settings.SavedWorkAreaHeightDip, savedScale),
            settings.PlacementXRatio,
            settings.PlacementYRatio,
            WindowAnchor.Margin * targetScale);
    }

    private static double? ScaleOptional(double? value, double scale) => value is { } finite && double.IsFinite(finite)
        ? finite * scale
        : null;

    private MonitorLayout ToPhysicalLayout(MonitorLayout layout)
    {
        MonitorInfo[] monitors = layout.All.Select(ToPhysicalMonitor).ToArray();
        MonitorInfo primary = monitors.FirstOrDefault(monitor =>
            string.Equals(monitor.DeviceName, layout.Primary.DeviceName, StringComparison.OrdinalIgnoreCase))
            ?? ToPhysicalMonitor(layout.Primary);
        if (monitors.Length == 0)
        {
            monitors = [primary];
        }

        if (_window.CurrentDpi is { } dpi && double.IsFinite(dpi) && dpi > 0)
        {
            MonitorInfo active = WindowAnchor.FindMonitorForWidget(CurrentWindowRect(), monitors, primary);
            MonitorInfo updated = active with { Dpi = dpi };
            for (int index = 0; index < monitors.Length; index++)
            {
                if (string.Equals(monitors[index].DeviceName, active.DeviceName, StringComparison.OrdinalIgnoreCase))
                {
                    monitors[index] = updated;
                    break;
                }
            }
            if (string.Equals(primary.DeviceName, active.DeviceName, StringComparison.OrdinalIgnoreCase))
            {
                primary = updated;
            }
        }
        return new MonitorLayout(monitors, primary);
    }

    private static MonitorInfo ToPhysicalMonitor(MonitorInfo monitor) => monitor with
    {
        WorkArea = monitor.PhysicalWorkArea ?? monitor.WorkArea,
    };

    private static MonitorInfo ResolveTarget(MonitorLayout layout, string? deviceName) =>
        deviceName is null
            ? layout.Primary
            : layout.All.FirstOrDefault(monitor => string.Equals(
                monitor.DeviceName,
                deviceName,
                StringComparison.OrdinalIgnoreCase)) ?? layout.Primary;

    private static double DpiScale(MonitorInfo monitor) =>
        double.IsFinite(monitor.Dpi) && monitor.Dpi > 0 ? monitor.Dpi / 96d : 1d;

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

    private void CapturePlacementIntent(Rect widget, IReadOnlyList<MonitorInfo> monitors, MonitorInfo primary)
    {
        MonitorInfo monitor = WindowAnchor.FindMonitorForWidget(widget, monitors, primary);
        if (WindowAnchor.TryCaptureIntent(widget, monitor, out WindowPlacementIntent intent))
        {
            _placementIntent = intent;
        }
    }

    private void UpdateDisplaySnapshot(IReadOnlyList<MonitorInfo> monitors, MonitorInfo primary, Rect widget) =>
        _displayLayoutSnapshot = new DisplayLayoutSnapshot(monitors.ToArray(), primary, widget);

    private bool ApplyWindowRect(Rect rect)
    {
        try
        {
            _applyingAutomaticPosition = true;
            bool applied = _window.TrySetPosition(rect.Left, rect.Top);
            if (!applied)
            {
                RecordDiagnostic("placement-native-move-failure", null);
            }
            return applied;
        }
        finally
        {
            _applyingAutomaticPosition = false;
        }
    }

    private void RecordDiagnostic(string category, Exception? exception)
    {
        try
        {
            _recordDiagnostic(category, exception);
        }
        catch
        {
            // An unavailable diagnostic sink must not prevent placement or cleanup.
        }
    }

    private Rect CurrentWindowRect() => _window.Bounds;

    private bool ApplyVerifiedPlacement(
        AppSettings settings,
        Func<MonitorLayout, Rect, Rect> compute,
        out MonitorLayout layout,
        out Rect actual)
    {
        layout = ToPhysicalLayout(_enumerateMonitors());
        actual = CurrentWindowRect();
        for (int attempt = 0; attempt < MaximumPlacementAttempts; attempt++)
        {
            if (!IsFinitePositive(actual.Width) || !IsFinitePositive(actual.Height))
            {
                RecordDiagnostic("placement-size-not-ready", null);
                return false;
            }
            Rect requested = compute(layout, actual);
            RecordDiagnostic("placement-attempt", new InvalidOperationException(
                $"Attempt={attempt + 1}; mode={settings.PlacementMode}; anchor={settings.PlacementAnchor}; requestedMonitor={settings.SavedMonitorDeviceName}; requested=({requested.Left},{requested.Top},{requested.Width},{requested.Height}); {_window.DiagnosticDescription}"));
            if (!ApplyWindowRect(requested))
            {
                return false;
            }
            actual = CurrentWindowRect();
            layout = ToPhysicalLayout(_enumerateMonitors());
            if (!IsFinitePositive(actual.Width) || !IsFinitePositive(actual.Height))
            {
                RecordDiagnostic("placement-size-not-ready", null);
                return false;
            }
            Rect expected = compute(layout, actual);
            MonitorInfo target = WindowAnchor.FindMonitorForWidget(expected, layout.All, layout.Primary);
            RecordDiagnostic("placement-result", new InvalidOperationException(
                $"Attempt={attempt + 1}; actualMonitor={target.DeviceName}; workArea={target.WorkArea}; expected={expected}; {_window.DiagnosticDescription}"));
            bool oversized = actual.Width > target.WorkArea.Width + 1 || actual.Height > target.WorkArea.Height + 1;
            if (Math.Abs(actual.Left - expected.Left) <= 1 && Math.Abs(actual.Top - expected.Top) <= 1
                && (oversized || Fits(actual, target.WorkArea, tolerance: 1)))
            {
                if (oversized)
                {
                    RecordDiagnostic("placement-widget-larger-than-work-area", null);
                }
                return true;
            }
        }
        RecordDiagnostic("placement-verification-failure", new InvalidOperationException(
            $"Attempts={MaximumPlacementAttempts}; monitor={settings.SavedMonitorDeviceName}; bounds=({actual.Left},{actual.Top},{actual.Right},{actual.Bottom})."));
        return false;
    }

    private static Rect ComputeRestoredRect(PlacementPreviewSession session, MonitorLayout layout, Rect bounds)
    {
        if (LayoutsEquivalent(session.Layout.All.ToArray(), layout.All) && IsInsideAny(session.OriginalBounds, layout.All))
        {
            MonitorInfo monitor = WindowAnchor.FindMonitorForWidget(session.OriginalBounds, layout.All, layout.Primary);
            Rect work = monitor.WorkArea;
            return new Rect(
                Math.Clamp(session.OriginalBounds.Left, work.Left, Math.Max(work.Left, work.Right - bounds.Width)),
                Math.Clamp(session.OriginalBounds.Top, work.Top, Math.Max(work.Top, work.Bottom - bounds.Height)),
                bounds.Width,
                bounds.Height);
        }
        return ComputeWindowRect(session.OriginalSettings, layout, bounds.Width, bounds.Height);
    }

    private static bool Fits(Rect bounds, Rect workArea, double tolerance) =>
        bounds.Width <= workArea.Width + tolerance
        && bounds.Height <= workArea.Height + tolerance
        && bounds.Left >= workArea.Left - tolerance
        && bounds.Top >= workArea.Top - tolerance
        && bounds.Right <= workArea.Right + tolerance
        && bounds.Bottom <= workArea.Bottom + tolerance;

    private static bool IsInsideAny(Rect bounds, IReadOnlyList<MonitorInfo> monitors) =>
        monitors.Any(monitor => Fits(bounds, monitor.WorkArea, tolerance: 1));

    private static bool IsFinitePositive(double value) => double.IsFinite(value) && value > 0;

    private static string AnchorLabel(WindowPlacementAnchor anchor) => anchor switch
    {
        WindowPlacementAnchor.TopLeft => "左上",
        WindowPlacementAnchor.BottomLeft => "左下",
        WindowPlacementAnchor.BottomRight => "右下",
        _ => "右上",
    };

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
        _previewSession = null;
        _previewSettings = null;
        _positionPersistTimer.Stop();
        _displayReflowTimer.Stop();
        _window.LocationChanged -= OnWindowLocationChanged;
        _window.Closing -= OnWindowClosing;
        _window.DisplayConfigurationChanged -= BeginDisplayReflow;
        _window.BoundsChanged -= BeginDisplayReflow;
        _window.UserMoveStarted -= OnUserMoveStarted;
        _window.UserMoveCompleted -= OnUserMoveCompleted;
        _surfaceLifetime?.Dispose();
    }

    private sealed record DisplayLayoutSnapshot(MonitorInfo[] Monitors, MonitorInfo Primary, Rect WidgetRect);
    private sealed record PlacementPreviewSession(
        AppSettings OriginalSettings,
        Rect OriginalBounds,
        MonitorLayout Layout,
        WindowPlacementIntent? OriginalPlacementIntent);
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
        _window.SizeChanged += HandleBoundsChanged;
        _window.DpiChanged += HandleDpiChanged;
        _window.ContentRendered += HandleContentRendered;
        _window.UserMoveStarted += HandleUserMoveStarted;
        _window.UserMoveCompleted += HandleUserMoveCompleted;
    }

    public Rect Bounds
    {
        get
        {
            IntPtr handle = _window.Handle;
            if (WindowPlacementNativeApi.TryGetBounds(handle, out Rect bounds))
            {
                return bounds;
            }

            double scale = WindowPlacementNativeApi.GetDpiForWindow(handle) / 96d;
            double width = _window.ActualWidth > 0 ? _window.ActualWidth : _window.Width;
            double height = _window.ActualHeight > 0 ? _window.ActualHeight : _window.Height;
            double left = double.IsFinite(_window.Left) ? _window.Left * scale : 0;
            double top = double.IsFinite(_window.Top) ? _window.Top * scale : 0;
            return new Rect(left, top, width * scale, height * scale);
        }
    }

    public bool IsLoaded => _window.IsLoaded;

    public string DiagnosticDescription =>
        $"RequestedDip=({_window.Width},{_window.Height}); actualDip=({_window.ActualWidth},{_window.ActualHeight}); visualScale=({System.Windows.Media.VisualTreeHelper.GetDpi(_window).DpiScaleX},{System.Windows.Media.VisualTreeHelper.GetDpi(_window).DpiScaleY}); transform={PresentationSource.FromVisual(_window)?.CompositionTarget?.TransformToDevice}; physicalBounds={Bounds}; windowDpi={CurrentDpi}";

    public double? CurrentDpi
    {
        get
        {
            uint dpi = WindowPlacementNativeApi.GetDpiForWindow(_window.Handle);
            return dpi > 0 ? dpi : null;
        }
    }

    public bool TrySetPosition(double left, double top) =>
        WindowPlacementNativeApi.TrySetPosition(_window.Handle, left, top);

    public event Action? LocationChanged;
    public event Action? Closing;
    public event Action? DisplayConfigurationChanged;
    public event Action? BoundsChanged;
    public event Action? UserMoveStarted;
    public event Action? UserMoveCompleted;

    private void HandleLocationChanged(object? sender, EventArgs e) => LocationChanged?.Invoke();
    private void HandleClosing(object? sender, CancelEventArgs e) => Closing?.Invoke();
    private void HandleDisplayConfigurationChanged() => DisplayConfigurationChanged?.Invoke();
    private void HandleBoundsChanged(object sender, SizeChangedEventArgs e) => BoundsChanged?.Invoke();
    private void HandleDpiChanged(object sender, System.Windows.DpiChangedEventArgs e) => BoundsChanged?.Invoke();
    private void HandleContentRendered(object? sender, EventArgs e) => BoundsChanged?.Invoke();
    private void HandleUserMoveStarted() => UserMoveStarted?.Invoke();
    private void HandleUserMoveCompleted() => UserMoveCompleted?.Invoke();

    public void Dispose()
    {
        _window.LocationChanged -= HandleLocationChanged;
        _window.Closing -= HandleClosing;
        _window.DisplayConfigurationChanged -= HandleDisplayConfigurationChanged;
        _window.SizeChanged -= HandleBoundsChanged;
        _window.DpiChanged -= HandleDpiChanged;
        _window.ContentRendered -= HandleContentRendered;
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
