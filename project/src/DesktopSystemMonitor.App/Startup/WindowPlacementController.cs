using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using DesktopSystemMonitor.App.Diagnostics;
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

    /// <summary>
    /// Brings the native window size in line with the required content size
    /// at the confirmed window DPI. Surfaces without separate native and
    /// logical sizes report <see cref="WindowSizeSyncResult.Unchecked"/>.
    /// </summary>
    WindowSizeSyncResult SynchronizeSize() => WindowSizeSyncResult.Unchecked;

    /// <summary>Read-only form of <see cref="SynchronizeSize"/>.</summary>
    WindowSizeSyncResult CheckSize() => WindowSizeSyncResult.Unchecked;
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
    private bool _placementInProgress;
    private VerifiedWindowState? _lastVerifiedState;
    private long _reflowWave;
    private bool _disposed;

    internal WindowPlacementController(
        MainWindow window,
        Func<AppSettings> getSettings,
        Action<Func<AppSettings, AppSettings>> updateSettings,
        Action<string, Exception?> recordDiagnostic,
        Dispatcher dispatcher)
    {
        var surface = new MainWindowPlacementSurface(window, recordDiagnostic);
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
        _window.BoundsChanged += OnWindowBoundsChanged;
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
        if (!IsSizeSynchronizedForCapture())
        {
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
        if (_disposed || !_window.IsLoaded || !IsSizeSynchronizedForCapture())
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
        if (!IsSizeSynchronizedForCapture())
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

    /// <summary>
    /// Size, DPI, and first-render notifications start a reflow wave unless
    /// they are echoes of the placement currently being verified, or report
    /// exactly the native bounds and DPI that the last wave already verified.
    /// The verification loop re-reads the window after its own resize and
    /// move, so those echoes cannot leave a mismatch behind.
    /// </summary>
    private void OnWindowBoundsChanged()
    {
        if (_disposed || _placementInProgress)
        {
            return;
        }
        if (!_displayReflow.Pending
            && _lastVerifiedState is VerifiedWindowState verified
            && verified.Bounds == CurrentWindowRect()
            && verified.Dpi == _window.CurrentDpi)
        {
            return;
        }
        BeginDisplayReflow();
    }

    private void BeginDisplayReflow()
    {
        if (_disposed)
        {
            return;
        }
        if (!_displayReflow.Pending)
        {
            _reflowWave++;
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

    /// <summary>
    /// Each attempt synchronizes the native size with the required content
    /// size first, then places the window from the re-read native bounds and
    /// verifies size and position together. A window that is inside the work
    /// area but still has a stale pixel size is not a success.
    /// </summary>
    private bool ApplyVerifiedPlacement(
        AppSettings settings,
        Func<MonitorLayout, Rect, Rect> compute,
        out MonitorLayout layout,
        out Rect actual)
    {
        _placementInProgress = true;
        try
        {
            bool placed = ApplyVerifiedPlacementCore(settings, compute, out layout, out actual);
            _lastVerifiedState = placed ? new VerifiedWindowState(actual, _window.CurrentDpi) : null;
            return placed;
        }
        finally
        {
            _placementInProgress = false;
        }
    }

    private bool ApplyVerifiedPlacementCore(
        AppSettings settings,
        Func<MonitorLayout, Rect, Rect> compute,
        out MonitorLayout layout,
        out Rect actual)
    {
        layout = ToPhysicalLayout(_enumerateMonitors());
        actual = CurrentWindowRect();
        WindowSizeSyncResult size = WindowSizeSyncResult.Unchecked;
        for (int attempt = 0; attempt < MaximumPlacementAttempts; attempt++)
        {
            size = SynchronizeSize(attempt, "before-move");
            actual = CurrentWindowRect();
            layout = ToPhysicalLayout(_enumerateMonitors());
            if (!IsFinitePositive(actual.Width) || !IsFinitePositive(actual.Height))
            {
                RecordDiagnostic("placement-size-not-ready", null);
                return false;
            }
            if (!size.IsSynchronized)
            {
                continue;
            }
            foreach (MonitorInfo monitor in layout.All)
            {
                // Separate bounded entries keep every monitor's measurements
                // intact even when the placement-attempt line is truncated.
                RecordDiagnostic("placement-monitor", new PlacementDiagnosticDetail(DescribeMonitor(monitor)));
            }
            Rect requested = compute(layout, actual);
            RecordDiagnostic("placement-attempt", new PlacementDiagnosticDetail(
                $"wave={_reflowWave}; attempt={attempt + 1}; mode={settings.PlacementMode}; anchor={settings.PlacementAnchor}; requestedMonitor={settings.SavedMonitorDeviceName}; requested=({FormatRect(requested)}); monitors={FormatMonitors(layout)}; {_window.DiagnosticDescription}"));
            if (!ApplyWindowRect(requested))
            {
                return false;
            }
            size = SynchronizeSize(attempt, "after-move");
            actual = CurrentWindowRect();
            layout = ToPhysicalLayout(_enumerateMonitors());
            if (!IsFinitePositive(actual.Width) || !IsFinitePositive(actual.Height))
            {
                RecordDiagnostic("placement-size-not-ready", null);
                return false;
            }
            if (!size.IsSynchronized)
            {
                continue;
            }
            Rect expected = compute(layout, actual);
            MonitorInfo target = WindowAnchor.FindMonitorForWidget(expected, layout.All, layout.Primary);
            RecordDiagnostic("placement-result", new PlacementDiagnosticDetail(
                $"wave={_reflowWave}; attempt={attempt + 1}; actualMonitor={target.DeviceName}; workArea=({FormatRect(target.WorkArea)}); expected=({FormatRect(expected)}); actual=({FormatRect(actual)}); size={size.Status}; {_window.DiagnosticDescription}"));
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
        if (!size.IsSynchronized)
        {
            RecordDiagnostic("placement-size-sync-failure", new PlacementDiagnosticDetail(
                $"wave={_reflowWave}; attempts={MaximumPlacementAttempts}; status={size.Status}; {size.Detail}"));
            return false;
        }
        RecordDiagnostic("placement-verification-failure", new PlacementDiagnosticDetail(
            $"wave={_reflowWave}; attempts={MaximumPlacementAttempts}; monitor={settings.SavedMonitorDeviceName}; bounds=({FormatRect(actual)})"));
        return false;
    }

    /// <summary>
    /// A frame whose native size does not match the required content size
    /// would capture a wrong right edge and placement ratio. Such a capture is
    /// skipped and a reflow wave repairs the size before the next save.
    /// </summary>
    private bool IsSizeSynchronizedForCapture()
    {
        WindowSizeSyncResult size;
        try
        {
            size = _window.CheckSize();
        }
        catch (Exception ex)
        {
            RecordDiagnostic("placement-size-sync-exception", ex);
            return false;
        }
        if (size.IsSynchronized)
        {
            return true;
        }
        RecordDiagnostic("placement-capture-size-mismatch", new PlacementDiagnosticDetail(
            $"wave={_reflowWave}; status={size.Status}; {size.Detail}"));
        BeginDisplayReflow();
        return false;
    }

    private WindowSizeSyncResult SynchronizeSize(int attempt, string phase)
    {
        WindowSizeSyncResult result;
        try
        {
            result = _window.SynchronizeSize();
        }
        catch (Exception ex)
        {
            RecordDiagnostic("placement-size-sync-exception", ex);
            return new WindowSizeSyncResult(WindowSizeSyncStatus.NotReady, "exception");
        }
        if (result.Status != WindowSizeSyncStatus.Matched)
        {
            RecordDiagnostic("placement-size-sync", new PlacementDiagnosticDetail(
                $"wave={_reflowWave}; attempt={attempt + 1}; phase={phase}; status={result.Status}; {result.Detail}"));
        }
        return result;
    }

    private static string FormatRect(Rect rect) =>
        $"{WindowPixelSize.Format(rect.Left)},{WindowPixelSize.Format(rect.Top)},{WindowPixelSize.Format(rect.Width)},{WindowPixelSize.Format(rect.Height)}";

    private static string FormatMonitors(MonitorLayout layout) => string.Join(
        " ",
        layout.All.Select(monitor =>
            $"{monitor.DeviceName}:work=({FormatRect(monitor.WorkArea)}):dpi={WindowPixelSize.Format(monitor.Dpi)}{(ReferenceEquals(monitor, layout.Primary) || string.Equals(monitor.DeviceName, layout.Primary.DeviceName, StringComparison.OrdinalIgnoreCase) ? ":primary" : string.Empty)}"));

    internal static string DescribeMonitor(MonitorInfo monitor) =>
        $"device={monitor.DeviceName}; physicalBounds={(monitor.PhysicalBounds is Rect bounds ? $"({FormatRect(bounds)})" : "unavailable")}; physicalWorkArea={(monitor.PhysicalWorkArea is Rect work ? $"({FormatRect(work)})" : "unavailable")}; effectiveDpi={(monitor.EffectiveDpi is double dpi && double.IsFinite(dpi) && dpi > 0 ? WindowPixelSize.Format(dpi) : "unavailable")}; placementDpi={WindowPixelSize.Format(monitor.Dpi)}";

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
        _window.BoundsChanged -= OnWindowBoundsChanged;
        _window.UserMoveStarted -= OnUserMoveStarted;
        _window.UserMoveCompleted -= OnUserMoveCompleted;
        _surfaceLifetime?.Dispose();
    }

    private sealed record DisplayLayoutSnapshot(MonitorInfo[] Monitors, MonitorInfo Primary, Rect WidgetRect);
    private readonly record struct VerifiedWindowState(Rect Bounds, double? Dpi);
    private sealed record PlacementPreviewSession(
        AppSettings OriginalSettings,
        Rect OriginalBounds,
        MonitorLayout Layout,
        WindowPlacementIntent? OriginalPlacementIntent);
}

internal sealed class MainWindowPlacementSurface : IWindowPlacementSurface, IDisposable
{
    private readonly MainWindow _window;
    private readonly Action<string, Exception?>? _recordDiagnostic;
    private readonly long _createdTimestamp = Stopwatch.GetTimestamp();

    internal MainWindowPlacementSurface(MainWindow window, Action<string, Exception?>? recordDiagnostic = null)
    {
        _window = window;
        _recordDiagnostic = recordDiagnostic;
        _window.LocationChanged += HandleLocationChanged;
        _window.Closing += HandleClosing;
        _window.DisplayConfigurationChanged += HandleDisplayConfigurationChanged;
        _window.SizeChanged += HandleBoundsChanged;
        _window.DpiChanged += HandleDpiChanged;
        _window.ContentRendered += HandleContentRendered;
        _window.UserMoveStarted += HandleUserMoveStarted;
        _window.UserMoveCompleted += HandleUserMoveCompleted;
        _window.NativeSizeMessageObserved += HandleNativeSizeMessage;
    }

    public Rect Bounds => ReadBounds(out _);

    private Rect ReadBounds(out bool native)
    {
        IntPtr handle = _window.Handle;
        if (WindowPlacementNativeApi.TryGetBounds(handle, out Rect bounds))
        {
            native = true;
            return bounds;
        }

        native = false;
        double scale = WindowPlacementNativeApi.GetDpiForWindow(handle) / 96d;
        double width = _window.ActualWidth > 0 ? _window.ActualWidth : _window.Width;
        double height = _window.ActualHeight > 0 ? _window.ActualHeight : _window.Height;
        double left = double.IsFinite(_window.Left) ? _window.Left * scale : 0;
        double top = double.IsFinite(_window.Top) ? _window.Top * scale : 0;
        return new Rect(left, top, width * scale, height * scale);
    }

    public bool IsLoaded => _window.IsLoaded;

    public string DiagnosticDescription
    {
        get
        {
            Rect bounds = ReadBounds(out bool native);
            System.Windows.DpiScale visual = System.Windows.Media.VisualTreeHelper.GetDpi(_window);
            System.Windows.Media.Matrix? transform = WindowSizeSynchronizer.DeviceTransform(_window.Handle);
            System.Windows.Size required = _window.RequiredWindowSizeDip;
            return $"requiredDip=({F(required.Width)},{F(required.Height)}); requestedDip=({F(_window.Width)},{F(_window.Height)}); actualDip=({F(_window.ActualWidth)},{F(_window.ActualHeight)}); visualScale=({F(visual.DpiScaleX)},{F(visual.DpiScaleY)}); transform=({F(transform?.M11 ?? double.NaN)},{F(transform?.M22 ?? double.NaN)}); physicalBounds=({F(bounds.Left)},{F(bounds.Top)},{F(bounds.Width)},{F(bounds.Height)}); boundsSource={(native ? "native" : "synthesized")}; windowDpi={CurrentDpi}";
        }
    }

    public double? CurrentDpi
    {
        get
        {
            uint dpi = WindowPlacementNativeApi.GetDpiForWindow(_window.Handle);
            return dpi > 0 ? dpi : null;
        }
    }

    public WindowSizeSyncResult SynchronizeSize()
    {
        _ = _window.Handle;
        return _window.SynchronizeWindowSize(apply: true);
    }

    public WindowSizeSyncResult CheckSize()
    {
        _ = _window.Handle;
        return _window.SynchronizeWindowSize(apply: false);
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

    private void HandleBoundsChanged(object sender, SizeChangedEventArgs e)
    {
        Trace($"SizeChanged; newDip=({F(e.NewSize.Width)},{F(e.NewSize.Height)})");
        BoundsChanged?.Invoke();
    }

    private void HandleDpiChanged(object sender, System.Windows.DpiChangedEventArgs e)
    {
        Trace($"DpiChanged; oldDpi={F(e.OldDpi.PixelsPerInchX)}; newDpi={F(e.NewDpi.PixelsPerInchX)}");
        BoundsChanged?.Invoke();
    }

    private void HandleContentRendered(object? sender, EventArgs e) => BoundsChanged?.Invoke();
    private void HandleUserMoveStarted() => UserMoveStarted?.Invoke();
    private void HandleUserMoveCompleted() => UserMoveCompleted?.Invoke();
    private void HandleNativeSizeMessage(string message) => Trace(message);

    /// <summary>
    /// Records the arrival order of native and WPF size/DPI notifications with
    /// a monotonic offset so a display-connect sequence can be reconstructed.
    /// </summary>
    private void Trace(string message)
    {
        if (_recordDiagnostic is null)
        {
            return;
        }
        double elapsedMs = Stopwatch.GetElapsedTime(_createdTimestamp).TotalMilliseconds;
        try
        {
            _recordDiagnostic("placement-trace", new PlacementDiagnosticDetail($"tMs={F(elapsedMs)}; event={message}"));
        }
        catch
        {
            // An unavailable diagnostic sink must not affect window notifications.
        }
    }

    private static string F(double value) => WindowPixelSize.Format(value);

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
        _window.NativeSizeMessageObserved -= HandleNativeSizeMessage;
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
