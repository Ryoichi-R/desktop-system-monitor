using System.Windows;
using System.Windows.Threading;
using DesktopSystemMonitor.Core.Settings;
using DesktopSystemMonitor.Windows.Window;

namespace DesktopSystemMonitor.App.Startup;

internal sealed class BackgroundLayerCoordinator : IDisposable
{
    private readonly MainWindow _mainWindow;
    private readonly BackgroundWindow _backgroundWindow;
    private readonly Dispatcher _dispatcher;
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

    private void CopyBounds()
    {
        _backgroundWindow.Left = _mainWindow.Left;
        _backgroundWindow.Top = _mainWindow.Top;
        _backgroundWindow.Width = _mainWindow.Width;
        _backgroundWindow.Height = _mainWindow.Height;
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
        }
    }

    internal void SetRepairSuspended(bool suspended)
    {
        ThrowIfDisposed();
        _backgroundWindow.SetRepairSuspended(suspended);
    }

    internal void Reapply(LayerRepairTrigger trigger)
    {
        ThrowIfDisposed();
        if (_mode == BackgroundPresentationMode.Split && _mainWindow.IsVisible)
        {
            SyncNow();
            _backgroundWindow.SetPresentationRequested(true, trigger);
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
