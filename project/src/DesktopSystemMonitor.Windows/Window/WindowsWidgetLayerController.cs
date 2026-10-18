using DesktopSystemMonitor.Core.Platform;

namespace DesktopSystemMonitor.Windows.Window;

/// <summary>
/// TopmostWindowRecoveryController（常に手前の自己修復）、ClickThroughHelper（クリック透過）、
/// BottomMostStrategy（デスクトップ最背面）を束ね、IWidgetLayerControllerへ適合させる。
/// WM_WINDOWPOSCHANGINGフックとの統合（WindowLayerRepairEngine相当）はWPF実装詳細として
/// MainWindow側に残し、ここでは持たない。
/// </summary>
public sealed class WindowsWidgetLayerController : IWidgetLayerController
{
    private readonly Func<IntPtr, TopmostWindowRecoveryController> _topmostFactory;
    private IntPtr _windowHandle;
    private TopmostWindowRecoveryController? _topmost;
    private WidgetLayerMode _mode = WidgetLayerMode.Normal;
    private bool _disposed;

    public WindowsWidgetLayerController()
        : this(handle => new TopmostWindowRecoveryController(handle))
    {
    }

    internal WindowsWidgetLayerController(Func<IntPtr, TopmostWindowRecoveryController> topmostFactory)
    {
        _topmostFactory = topmostFactory;
    }

    public WidgetLayerHealth Health => _topmost is { } topmost ? MapHealth(topmost.Health) : default;

    public event Action<WidgetLayerHealth>? HealthChanged;

    public void Attach(IntPtr nativeWindowHandle)
    {
        ThrowIfDisposed();
        if (_windowHandle != IntPtr.Zero)
            throw new InvalidOperationException("Already attached to a window handle.");
        _windowHandle = nativeWindowHandle;
        _topmost = _topmostFactory(nativeWindowHandle);
        _topmost.HealthChanged += health => HealthChanged?.Invoke(MapHealth(health));
    }

    public void SetLayerMode(WidgetLayerMode mode)
    {
        ThrowIfDisposed();
        EnsureAttached();
        _mode = mode;
        _topmost!.SetEnabled(mode == WidgetLayerMode.AlwaysOnTop);
        if (mode != WidgetLayerMode.AlwaysOnTop)
            BottomMostStrategy.Apply(_windowHandle, ToLayerStrategy(mode));
    }

    public void SetClickThrough(bool enabled)
    {
        ThrowIfDisposed();
        EnsureAttached();
        ClickThroughHelper.SetClickThrough(_windowHandle, enabled);
    }

    public bool TryRecoverLayer()
    {
        ThrowIfDisposed();
        if (_windowHandle == IntPtr.Zero) return false;
        return _mode == WidgetLayerMode.AlwaysOnTop
            ? _topmost!.TryRecover()
            : BottomMostStrategy.Apply(_windowHandle, ToLayerStrategy(_mode));
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _topmost?.Dispose();
        HealthChanged = null;
    }

    private void EnsureAttached()
    {
        if (_windowHandle == IntPtr.Zero)
            throw new InvalidOperationException("Attach must be called before use.");
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);

    private static LayerStrategy ToLayerStrategy(WidgetLayerMode mode) => mode switch
    {
        WidgetLayerMode.AlwaysOnTop => LayerStrategy.TopMost,
        WidgetLayerMode.AlwaysOnBottom => LayerStrategy.BottomMost,
        _ => LayerStrategy.Normal,
    };

    private static WidgetLayerHealth MapHealth(TopmostRecoveryHealth health) =>
        new(health.IsDegraded, health.Operation, health.ErrorCode);
}
