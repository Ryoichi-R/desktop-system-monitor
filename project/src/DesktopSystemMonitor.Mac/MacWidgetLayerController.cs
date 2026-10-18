using DesktopSystemMonitor.Core.Platform;
using DesktopSystemMonitor.Mac.Interop;

namespace DesktopSystemMonitor.Mac;

/// <summary>
/// NSWindowの層制御をAvaloniaから分離する。macOSは宣言的なlevelを維持するため、
/// Windowsのような周期的なWndProc修復ループを持たない。
/// </summary>
public sealed class MacWidgetLayerController : IWidgetLayerController
{
    private const nuint WidgetCollectionBehavior = 1 | 16 | 64 | 256;
    private readonly IMacWindowInterop _interop;
    private IntPtr _window;
    private WidgetLayerMode _mode = WidgetLayerMode.Normal;
    private bool _clickThrough;
    private bool _disposed;
    private WidgetLayerHealth _health;

    public MacWidgetLayerController() : this(new MacWindowInterop())
    {
    }

    internal MacWidgetLayerController(IMacWindowInterop interop)
    {
        _interop = interop ?? throw new ArgumentNullException(nameof(interop));
    }

    public WidgetLayerHealth Health => _health;

    public event Action<WidgetLayerHealth>? HealthChanged;

    public void Attach(IntPtr nativeWindowHandle)
    {
        ThrowIfDisposed();
        if (nativeWindowHandle == IntPtr.Zero) throw new ArgumentException("A native window handle is required.", nameof(nativeWindowHandle));
        if (_window != IntPtr.Zero) throw new InvalidOperationException("Already attached to a window handle.");
        _window = nativeWindowHandle;
        Apply(() => _interop.SetCollectionBehavior(_window, WidgetCollectionBehavior), "setCollectionBehavior");
    }

    public void SetLayerMode(WidgetLayerMode mode)
    {
        ThrowIfDisposed();
        EnsureAttached();
        _mode = mode;
        int level = mode switch
        {
            WidgetLayerMode.AlwaysOnTop => _interop.FloatingWindowLevel,
            WidgetLayerMode.AlwaysOnBottom => _interop.DesktopWindowLevel,
            _ => _interop.NormalWindowLevel,
        };
        Apply(() => _interop.SetLevel(_window, level), "setLevel");
    }

    public void SetClickThrough(bool enabled)
    {
        ThrowIfDisposed();
        EnsureAttached();
        _clickThrough = enabled;
        Apply(() => _interop.SetIgnoresMouseEvents(_window, enabled), "setIgnoresMouseEvents");
    }

    public bool TryRecoverLayer()
    {
        ThrowIfDisposed();
        if (_window == IntPtr.Zero) return false;
        SetLayerMode(_mode);
        bool layerRecovered = !_health.IsDegraded;
        SetClickThrough(_clickThrough);
        return layerRecovered && !_health.IsDegraded;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        HealthChanged = null;
        _window = IntPtr.Zero;
    }

    private void Apply(Action action, string operation)
    {
        try
        {
            action();
            SetHealth(default);
        }
        catch (Exception exception) when (exception is DllNotFoundException or EntryPointNotFoundException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            SetHealth(new WidgetLayerHealth(true, operation, exception.HResult));
        }
    }

    private void SetHealth(WidgetLayerHealth health)
    {
        if (_health == health) return;
        _health = health;
        HealthChanged?.Invoke(health);
    }

    private void EnsureAttached()
    {
        if (_window == IntPtr.Zero) throw new InvalidOperationException("Attach must be called before use.");
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);
}
