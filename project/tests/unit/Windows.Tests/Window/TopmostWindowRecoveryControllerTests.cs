using DesktopSystemMonitor.Windows.Window;
using Xunit;

namespace DesktopSystemMonitor.Windows.Tests.Window;

public sealed class TopmostWindowRecoveryControllerTests
{
    [Fact]
    public void enabling_registers_foreground_move_size_and_desktop_hooks()
    {
        var interop = new FakeTopmostRecoveryInterop();
        using var controller = new TopmostWindowRecoveryController(new IntPtr(10), interop);

        controller.SetEnabled(true);

        Assert.Equal(3, interop.HookRegistrations.Count);
        Assert.All(interop.HookRegistrations, registration =>
        {
            Assert.Equal(TopmostWindowRecoveryController.HookFlags, registration.Flags);
            Assert.NotEqual(IntPtr.Zero, registration.CallbackHandle);
        });
        Assert.Equal(3, controller.HookHandles.Count);
    }

    [Fact]
    public void win_event_requests_recovery_and_disable_unregisters_all_hooks()
    {
        var interop = new FakeTopmostRecoveryInterop();
        using var controller = new TopmostWindowRecoveryController(new IntPtr(10), interop);
        int requests = 0;
        controller.RecoveryRequested += () => requests++;

        controller.SetEnabled(true);
        interop.RaiseEvent();
        controller.SetEnabled(false);
        interop.RaiseEvent();

        Assert.Equal(1, requests);
        Assert.Equal(3, interop.UnhookedHandles.Count);
        Assert.Empty(controller.HookHandles);
    }

    [Fact]
    public void recovery_uses_topmost_without_move_size_or_activation()
    {
        var interop = new FakeTopmostRecoveryInterop();
        using var controller = new TopmostWindowRecoveryController(new IntPtr(10), interop);
        controller.SetEnabled(true);

        Assert.True(controller.TryRecover());

        var call = Assert.Single(interop.SetWindowPositionCalls);
        Assert.Equal(new IntPtr(10), call.WindowHandle);
        Assert.Equal(WindowInterop.HWND_TOPMOST, call.InsertAfter);
        Assert.Equal(
            WindowInterop.SWP_NOMOVE | WindowInterop.SWP_NOSIZE | WindowInterop.SWP_NOACTIVATE,
            call.Flags);
    }

    [Fact]
    public void hook_registration_failure_cleans_up_and_reports_health()
    {
        var interop = new FakeTopmostRecoveryInterop { FailRegistrationNumber = 2, LastErrorCode = 5 };
        using var controller = new TopmostWindowRecoveryController(new IntPtr(10), interop);
        var health = new List<TopmostRecoveryHealth>();
        controller.HealthChanged += health.Add;

        controller.SetEnabled(true);

        var failure = Assert.Single(health);
        Assert.True(failure.IsDegraded);
        Assert.Equal("SetWinEventHook", failure.Operation);
        Assert.Equal(5, failure.ErrorCode);
        Assert.Equal(1, failure.ConsecutiveFailures);
        Assert.Single(interop.UnhookedHandles);
        Assert.Empty(controller.HookHandles);
    }

    [Fact]
    public void recovery_failure_reports_health()
    {
        var interop = new FakeTopmostRecoveryInterop { SetWindowPositionResult = false, LastErrorCode = 1400 };
        using var controller = new TopmostWindowRecoveryController(new IntPtr(10), interop);
        var health = new List<TopmostRecoveryHealth>();
        controller.HealthChanged += health.Add;
        controller.SetEnabled(true);

        Assert.False(controller.TryRecover());

        var failure = Assert.Single(health);
        Assert.True(failure.IsDegraded);
        Assert.Equal("SetWindowPos", failure.Operation);
        Assert.Equal(1400, failure.ErrorCode);
    }

    private sealed class FakeTopmostRecoveryInterop : ITopmostRecoveryInterop
    {
        private int _registrationCount;

        public int FailRegistrationNumber { get; init; }
        public int LastErrorCode { get; set; }
        public bool SetWindowPositionResult { get; init; } = true;
        public List<HookRegistration> HookRegistrations { get; } = [];
        public List<IntPtr> UnhookedHandles { get; } = [];
        public List<WindowPositionCall> SetWindowPositionCalls { get; } = [];

        public IntPtr SetWinEventHook(
            uint eventMin,
            uint eventMax,
            uint flags,
            WindowInterop.WinEventDelegate callback)
        {
            _registrationCount++;
            if (_registrationCount == FailRegistrationNumber)
            {
                return IntPtr.Zero;
            }

            IntPtr handle = new(_registrationCount);
            HookRegistrations.Add(new HookRegistration(eventMin, eventMax, flags, handle, callback));
            return handle;
        }

        public bool UnhookWinEvent(IntPtr hookHandle)
        {
            UnhookedHandles.Add(hookHandle);
            return true;
        }

        public bool SetWindowPos(IntPtr windowHandle, IntPtr insertAfter, uint flags)
        {
            SetWindowPositionCalls.Add(new WindowPositionCall(windowHandle, insertAfter, flags));
            return SetWindowPositionResult;
        }

        public void RaiseEvent()
        {
            if (HookRegistrations.Count > 0)
            {
                HookRegistrations[0].Callback(
                    HookRegistrations[0].CallbackHandle,
                    WindowInterop.EVENT_SYSTEM_FOREGROUND,
                    new IntPtr(20),
                    0,
                    0,
                    0,
                    0);
            }
        }
    }

    private readonly record struct HookRegistration(
        uint EventMin,
        uint EventMax,
        uint Flags,
        IntPtr CallbackHandle,
        WindowInterop.WinEventDelegate Callback);

    private readonly record struct WindowPositionCall(
        IntPtr WindowHandle,
        IntPtr InsertAfter,
        uint Flags);
}
