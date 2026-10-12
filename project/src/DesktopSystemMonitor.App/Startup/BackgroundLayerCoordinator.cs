using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using DesktopSystemMonitor.App.Diagnostics;
using DesktopSystemMonitor.Core.Settings;
using DesktopSystemMonitor.Windows.Window;

namespace DesktopSystemMonitor.App.Startup;

internal sealed class BackgroundLayerCoordinator : IDisposable
{
    private const int MaximumNativeAlignAttempts = 3;
    private readonly MainWindow _mainWindow;
    private readonly BackgroundWindow _backgroundWindow;
    private readonly Dispatcher _dispatcher;
    private readonly Action<string, Exception?> _recordDiagnostic;
    private AppSettings _settings = new AppSettings().Normalized();
    private BackgroundPresentationMode _mode;
    private DispatcherOperation? _syncOperation;
    private bool _disposed;

    internal BackgroundLayerCoordinator(
        MainWindow mainWindow,
        Action<string, Exception?> recordDiagnostic,
        Dispatcher dispatcher,
        IWindowLayerApi? layerApi = null)
    {
        _mainWindow = mainWindow ?? throw new ArgumentNullException(nameof(mainWindow));
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        _recordDiagnostic = recordDiagnostic ?? throw new ArgumentNullException(nameof(recordDiagnostic));
        _backgroundWindow = new BackgroundWindow(recordDiagnostic, layerApi)
        {
            WindowStartupLocation = WindowStartupLocation.Manual,
        };
        _mainWindow.LocationChanged += OnBoundsChanged;
        _mainWindow.SizeChanged += OnBoundsChanged;
        _mainWindow.DpiChanged += OnDpiChanged;
        _mainWindow.IsVisibleChanged += OnVisibilityChanged;
        _mainWindow.UserMoveStarted += OnUserMoveStarted;
        _mainWindow.UserMoveCompleted += OnUserMoveCompleted;
        _mainWindow.Closed += OnMainWindowClosed;
    }

    internal BackgroundPresentationMode Mode => _mode;
    internal BackgroundWindow BackgroundWindow => _backgroundWindow;

    internal void Apply(AppSettings settings, LayerRepairTrigger trigger)
    {
        ThrowIfDisposed();
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        BackgroundPresentationMode requestedMode = BackgroundPresentationPolicy.Evaluate(settings);

        if (requestedMode != BackgroundPresentationMode.Split)
        {
            _backgroundWindow.SetPresentationRequested(false, trigger);
            _mainWindow.SetInlineBackground(settings, requestedMode);
            _mode = requestedMode;
            return;
        }

        _mainWindow.SetInlineBackground(settings, BackgroundPresentationMode.Split);
        _mode = BackgroundPresentationMode.Split;
        _backgroundWindow.ApplyAppearance(settings);
        SyncNow();
        _backgroundWindow.SetPresentationRequested(_mainWindow.IsVisible, trigger);
        AlignNativeBounds();
    }

    internal void SyncNow()
    {
        ThrowIfDisposed();
        if (_syncOperation?.Status == DispatcherOperationStatus.Pending)
        {
            _syncOperation.Abort();
        }
        _syncOperation = null;
        CopyBounds();
    }

    /// <summary>
    /// Copies the requested DIP bounds, which is exact while both windows share
    /// one DPI, and then aligns the background to the information window's
    /// native pixel rectangle. The DIP copy alone is wrong whenever the two
    /// windows temporarily sit at different DPIs or the information window's
    /// native frame no longer matches its requested size.
    /// </summary>
    private void CopyBounds()
    {
        _backgroundWindow.Left = _mainWindow.Left;
        _backgroundWindow.Top = _mainWindow.Top;
        _backgroundWindow.Width = _mainWindow.Width;
        _backgroundWindow.Height = _mainWindow.Height;
        AlignNativeBounds();
    }

    private void AlignNativeBounds()
    {
        IntPtr mainHandle = new WindowInteropHelper(_mainWindow).Handle;
        IntPtr backgroundHandle = new WindowInteropHelper(_backgroundWindow).Handle;
        if (mainHandle == IntPtr.Zero
            || backgroundHandle == IntPtr.Zero
            || !TryGetConfirmedDpi(mainHandle, out uint mainDpi)
            || !WindowPlacementNativeApi.TryGetBounds(mainHandle, out var mainBounds))
        {
            return;
        }

        for (int attempt = 0; attempt <= MaximumNativeAlignAttempts; attempt++)
        {
            bool backgroundRead = WindowPlacementNativeApi.TryGetBounds(backgroundHandle, out var backgroundBounds);
            bool backgroundDpiConfirmed = TryGetConfirmedDpi(backgroundHandle, out uint backgroundDpi);
            if (backgroundRead
                && backgroundDpiConfirmed
                && backgroundDpi == mainDpi
                && BoundsMatch(backgroundBounds, mainBounds))
            {
                return;
            }
            if (attempt == MaximumNativeAlignAttempts)
            {
                string background = backgroundRead ? FormatRect(backgroundBounds) : "none";
                RecordDiagnostic("background-native-bounds-mismatch", new PlacementDiagnosticDetail(
                    $"attempts={MaximumNativeAlignAttempts}; mainPx=({FormatRect(mainBounds)}); mainDpi={mainDpi}; backgroundPx=({background}); backgroundDpi={backgroundDpi}; backgroundDpiConfirmed={backgroundDpiConfirmed}"));
                return;
            }
            // A move onto another monitor makes WPF apply the DPI-scaled
            // suggested rectangle first; the next pass restores the exact size.
            if (!WindowPlacementNativeApi.TrySetBounds(backgroundHandle, mainBounds))
            {
                RecordDiagnostic("background-native-bounds-failure", null);
                return;
            }
        }
    }

    private static bool TryGetConfirmedDpi(IntPtr handle, out uint dpi)
    {
        dpi = WindowPlacementNativeApi.GetDpiForWindow(handle);
        System.Windows.Media.Matrix? transform = WindowSizeSynchronizer.DeviceTransform(handle);
        return transform is System.Windows.Media.Matrix deviceTransform
            && WindowPixelSize.TryGetScale(dpi, deviceTransform.M11, deviceTransform.M22, out _);
    }

    private static bool BoundsMatch(DesktopSystemMonitor.Core.Layout.Rect left, DesktopSystemMonitor.Core.Layout.Rect right) =>
        Math.Abs(left.Left - right.Left) <= WindowPixelSize.TolerancePx
        && Math.Abs(left.Top - right.Top) <= WindowPixelSize.TolerancePx
        && Math.Abs(left.Width - right.Width) <= WindowPixelSize.TolerancePx
        && Math.Abs(left.Height - right.Height) <= WindowPixelSize.TolerancePx;

    private static string FormatRect(DesktopSystemMonitor.Core.Layout.Rect rect) =>
        $"{WindowPixelSize.Format(rect.Left)},{WindowPixelSize.Format(rect.Top)},{WindowPixelSize.Format(rect.Width)},{WindowPixelSize.Format(rect.Height)}";

    private void RecordDiagnostic(string category, Exception? exception)
    {
        try
        {
            _recordDiagnostic(category, exception);
        }
        catch
        {
            // An unavailable diagnostic sink must not prevent background tracking.
        }
    }

    internal void SetWidgetVisible(bool visible)
    {
        ThrowIfDisposed();
        if (!visible)
        {
            _backgroundWindow.SetPresentationRequested(false, LayerRepairTrigger.VisibilityChanged);
            _mainWindow.Hide();
            return;
        }

        _mainWindow.Show();
        if (_mode == BackgroundPresentationMode.Split)
        {
            _backgroundWindow.ApplyAppearance(_settings);
            SyncNow();
            _backgroundWindow.SetPresentationRequested(true, LayerRepairTrigger.VisibilityChanged);
            AlignNativeBounds();
        }
    }

    internal void SetRepairSuspended(bool suspended, LayerRepairTrigger trigger)
    {
        ThrowIfDisposed();
        _backgroundWindow.SetRepairSuspended(suspended, trigger);
    }

    internal void Reapply(LayerRepairTrigger trigger)
    {
        ThrowIfDisposed();
        if (_mode == BackgroundPresentationMode.Split && _mainWindow.IsVisible)
        {
            SyncNow();
            _backgroundWindow.SetPresentationRequested(true, trigger);
            AlignNativeBounds();
        }
    }

    private void OnBoundsChanged(object? sender, EventArgs e) => ScheduleSync();

    private void OnDpiChanged(object sender, System.Windows.DpiChangedEventArgs e) => ScheduleSync();

    private void ScheduleSync()
    {
        if (_disposed
            || _mode != BackgroundPresentationMode.Split
            || _syncOperation?.Status is DispatcherOperationStatus.Pending or DispatcherOperationStatus.Executing)
        {
            return;
        }
        _syncOperation = _dispatcher.BeginInvoke(DispatcherPriority.Render, () =>
        {
            if (!_disposed && _mode == BackgroundPresentationMode.Split)
            {
                CopyBounds();
            }
            _syncOperation = null;
        });
    }

    private void OnUserMoveStarted()
    {
        if (!_disposed && _mode == BackgroundPresentationMode.Split)
        {
            SyncNow();
        }
    }

    private void OnUserMoveCompleted()
    {
        if (!_disposed && _mode == BackgroundPresentationMode.Split)
        {
            SyncNow();
        }
    }

    private void OnVisibilityChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (_disposed || _mode != BackgroundPresentationMode.Split)
        {
            return;
        }
        if (_mainWindow.IsVisible)
        {
            _backgroundWindow.ApplyAppearance(_settings);
            SyncNow();
            _backgroundWindow.SetPresentationRequested(true, LayerRepairTrigger.VisibilityChanged);
            AlignNativeBounds();
        }
        else
        {
            _backgroundWindow.SetPresentationRequested(false, LayerRepairTrigger.VisibilityChanged);
        }
    }

    private void OnMainWindowClosed(object? sender, EventArgs e) => Dispose();

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;
        _mainWindow.LocationChanged -= OnBoundsChanged;
        _mainWindow.SizeChanged -= OnBoundsChanged;
        _mainWindow.DpiChanged -= OnDpiChanged;
        _mainWindow.IsVisibleChanged -= OnVisibilityChanged;
        _mainWindow.UserMoveStarted -= OnUserMoveStarted;
        _mainWindow.UserMoveCompleted -= OnUserMoveCompleted;
        _mainWindow.Closed -= OnMainWindowClosed;
        if (_syncOperation?.Status == DispatcherOperationStatus.Pending)
        {
            _syncOperation.Abort();
        }
        _syncOperation = null;
        _backgroundWindow.Close();
    }
}
