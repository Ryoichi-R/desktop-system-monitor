using System.Runtime.InteropServices;

namespace DesktopSystemMonitor.Windows.Window;

internal readonly record struct TopmostRecoveryHealth(
    bool IsDegraded,
    string? Operation,
    int ErrorCode,
    int ConsecutiveFailures);

internal interface ITopmostRecoveryInterop
{
    int LastErrorCode { get; }

    IntPtr SetWinEventHook(
        uint eventMin,
        uint eventMax,
        uint flags,
        WindowInterop.WinEventDelegate callback);

    bool UnhookWinEvent(IntPtr hookHandle);

    bool SetWindowPos(IntPtr windowHandle, IntPtr insertAfter, uint flags);
}

internal sealed class NativeTopmostRecoveryInterop : ITopmostRecoveryInterop
{
    public static NativeTopmostRecoveryInterop Instance { get; } = new();

    public int LastErrorCode { get; private set; }

    public IntPtr SetWinEventHook(
        uint eventMin,
        uint eventMax,
        uint flags,
        WindowInterop.WinEventDelegate callback)
    {
        Marshal.SetLastPInvokeError(0);
        IntPtr hook = WindowInterop.SetWinEventHook(
            eventMin,
            eventMax,
            IntPtr.Zero,
            callback,
            0,
            0,
            flags);
        LastErrorCode = hook == IntPtr.Zero ? Marshal.GetLastPInvokeError() : 0;
        return hook;
    }

    public bool UnhookWinEvent(IntPtr hookHandle)
    {
        Marshal.SetLastPInvokeError(0);
        bool result = WindowInterop.UnhookWinEvent(hookHandle);
        LastErrorCode = result ? 0 : Marshal.GetLastPInvokeError();
        return result;
    }

    public bool SetWindowPos(IntPtr windowHandle, IntPtr insertAfter, uint flags)
    {
        Marshal.SetLastPInvokeError(0);
        bool result = WindowInterop.SetWindowPos(windowHandle, insertAfter, 0, 0, 0, 0, flags);
        LastErrorCode = result ? 0 : Marshal.GetLastPInvokeError();
        return result;
    }
}

/// <summary>
/// Re-applies the topmost Z-order after an external window changes the
/// topmost band. The controller never activates or moves the widget.
/// </summary>
internal sealed class TopmostWindowRecoveryController : IDisposable
{
    private readonly IntPtr _windowHandle;
    private readonly ITopmostRecoveryInterop _interop;
    private readonly WindowInterop.WinEventDelegate _callback;
    private readonly List<IntPtr> _hookHandles = [];
    private readonly int _registrationThreadId;
    private bool _enabled;
    private bool _disposed;
    private TopmostRecoveryHealth _health;

    internal TopmostWindowRecoveryController(IntPtr windowHandle)
        : this(windowHandle, NativeTopmostRecoveryInterop.Instance)
    {
    }

    internal TopmostWindowRecoveryController(
        IntPtr windowHandle,
        ITopmostRecoveryInterop interop)
    {
        ArgumentOutOfRangeException.ThrowIfZero(windowHandle, nameof(windowHandle));
        _windowHandle = windowHandle;
        _interop = interop ?? throw new ArgumentNullException(nameof(interop));
        _callback = OnWinEvent;
        _registrationThreadId = Environment.CurrentManagedThreadId;
    }

    internal event Action? RecoveryRequested;
    internal event Action<TopmostRecoveryHealth>? HealthChanged;
    internal TopmostRecoveryHealth Health => _health;

    internal static uint RecoveryFlags => WindowInterop.SWP_NOMOVE
        | WindowInterop.SWP_NOSIZE
        | WindowInterop.SWP_NOACTIVATE;

    internal static uint HookFlags => WindowInterop.WINEVENT_OUTOFCONTEXT
        | WindowInterop.WINEVENT_SKIPOWNPROCESS;

    internal IReadOnlyList<IntPtr> HookHandles => _hookHandles;

    internal void SetEnabled(bool enabled)
    {
        ThrowIfDisposed();
        if (!enabled)
        {
            _enabled = false;
            UnregisterHooks();
            SetHealthy();
            return;
        }

        if (_enabled)
        {
            return;
        }

        _enabled = true;
        foreach ((uint min, uint max) in new[]
        {
            (WindowInterop.EVENT_SYSTEM_FOREGROUND, WindowInterop.EVENT_SYSTEM_FOREGROUND),
            (WindowInterop.EVENT_SYSTEM_MOVESIZEEND, WindowInterop.EVENT_SYSTEM_MOVESIZEEND),
            (WindowInterop.EVENT_SYSTEM_DESKTOPSWITCH, WindowInterop.EVENT_SYSTEM_DESKTOPSWITCH),
        })
        {
            IntPtr hook = IntPtr.Zero;
            try
            {
                hook = _interop.SetWinEventHook(
                    min,
                    max,
                    HookFlags,
                    _callback);
            }
            catch
            {
                hook = IntPtr.Zero;
            }

            if (hook == IntPtr.Zero)
            {
                int errorCode = _interop.LastErrorCode;
                _enabled = false;
                UnregisterHooks();
                SetFailure("SetWinEventHook", errorCode);
                return;
            }

            _hookHandles.Add(hook);
        }

        SetHealthy();
    }

    internal bool TryRecover()
    {
        if (_disposed || !_enabled)
        {
            return false;
        }

        try
        {
            bool recovered = _interop.SetWindowPos(
                _windowHandle,
                WindowInterop.HWND_TOPMOST,
                RecoveryFlags);
            if (recovered)
            {
                SetHealthy();
            }
            else
            {
                SetFailure("SetWindowPos", _interop.LastErrorCode);
            }

            return recovered;
        }
        catch
        {
            SetFailure("SetWindowPos", _interop.LastErrorCode);
            return false;
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        if (Environment.CurrentManagedThreadId != _registrationThreadId)
        {
            throw new InvalidOperationException(
                "Topmost hooks must be released on their registration thread.");
        }

        _disposed = true;
        _enabled = false;
        UnregisterHooks();
        RecoveryRequested = null;
        HealthChanged = null;
    }

    private void OnWinEvent(
        IntPtr hookHandle,
        uint eventType,
        IntPtr windowHandle,
        int objectId,
        int childId,
        uint eventThreadId,
        uint eventTime)
    {
        if (_disposed || !_enabled)
        {
            return;
        }

        try
        {
            RecoveryRequested?.Invoke();
        }
        catch
        {
            // WinEvent callbacks must never leak subscriber failures to user32.
        }
    }

    private void UnregisterHooks()
    {
        foreach (IntPtr hook in _hookHandles.ToArray())
        {
            try
            {
                _ = _interop.UnhookWinEvent(hook);
            }
            catch
            {
                // Best effort during disable and shutdown.
            }
        }

        _hookHandles.Clear();
    }

    private void SetHealthy()
    {
        if (!_health.IsDegraded && _health.ConsecutiveFailures == 0)
        {
            return;
        }

        _health = default;
        try
        {
            HealthChanged?.Invoke(_health);
        }
        catch
        {
            // Diagnostics must not destabilize the hook callback path.
        }
    }

    private void SetFailure(string operation, int errorCode)
    {
        _health = new TopmostRecoveryHealth(
            IsDegraded: true,
            Operation: operation,
            ErrorCode: errorCode,
            ConsecutiveFailures: _health.ConsecutiveFailures + 1);
        try
        {
            HealthChanged?.Invoke(_health);
        }
        catch
        {
            // Diagnostics must not destabilize the hook callback path.
        }
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);
}
