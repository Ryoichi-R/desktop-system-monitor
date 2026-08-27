using DesktopSystemMonitor.Core.Platform;
using DesktopSystemMonitor.Windows.Window;
using Xunit;

namespace DesktopSystemMonitor.Windows.Tests.Window;

public sealed class WindowsWidgetLayerControllerTests
{
    [Fact]
    public void SetLayerModeBeforeAttachThrows()
    {
        using var controller = CreateController(out _);

        Assert.Throws<InvalidOperationException>(() => controller.SetLayerMode(WidgetLayerMode.AlwaysOnTop));
    }

    [Fact]
    public void AttachingTwiceThrows()
    {
        using var controller = CreateController(out _);
        controller.Attach(new IntPtr(10));

        Assert.Throws<InvalidOperationException>(() => controller.Attach(new IntPtr(20)));
    }

    [Fact]
    public void AlwaysOnTopEnablesTopmostRecoveryHooks()
    {
        using var controller = CreateController(out FakeTopmostRecoveryInterop interop);
        controller.Attach(new IntPtr(10));

        controller.SetLayerMode(WidgetLayerMode.AlwaysOnTop);

        Assert.Equal(3, interop.HookCount);
    }

    [Fact]
    public void TryRecoverLayerDelegatesToTopmostWhenModeIsAlwaysOnTop()
    {
        using var controller = CreateController(out FakeTopmostRecoveryInterop interop);
        controller.Attach(new IntPtr(10));
        controller.SetLayerMode(WidgetLayerMode.AlwaysOnTop);

        bool recovered = controller.TryRecoverLayer();

        Assert.True(recovered);
        Assert.Equal(1, interop.SetWindowPosCallCount);
    }

    [Fact]
    public void TryRecoverLayerReturnsFalseBeforeAttach()
    {
        using var controller = CreateController(out _);

        Assert.False(controller.TryRecoverLayer());
    }

    [Fact]
    public void HealthChangedPropagatesFromTheUnderlyingTopmostController()
    {
        using var controller = CreateController(out FakeTopmostRecoveryInterop interop);
        var received = new List<WidgetLayerHealth>();
        controller.HealthChanged += received.Add;
        controller.Attach(new IntPtr(10));
        controller.SetLayerMode(WidgetLayerMode.AlwaysOnTop);
        interop.SetWindowPosResult = false;
        interop.LastErrorCode = 1400;

        controller.TryRecoverLayer();

        Assert.Contains(received, health => health.IsDegraded && health.ErrorCode == 1400);
        Assert.True(controller.Health.IsDegraded);
    }

    [Fact]
    public void DisposedControllerThrowsOnFurtherUse()
    {
        var controller = CreateController(out _);
        controller.Attach(new IntPtr(10));
        controller.Dispose();

        Assert.Throws<ObjectDisposedException>(() => controller.SetLayerMode(WidgetLayerMode.AlwaysOnTop));
    }

    private static WindowsWidgetLayerController CreateController(out FakeTopmostRecoveryInterop interop)
    {
        var captured = new FakeTopmostRecoveryInterop();
        interop = captured;
        return new WindowsWidgetLayerController(handle => new TopmostWindowRecoveryController(handle, captured));
    }

    private sealed class FakeTopmostRecoveryInterop : ITopmostRecoveryInterop
    {
        private int _nextHook = 1;

        public int HookCount { get; private set; }
        public int SetWindowPosCallCount { get; private set; }
        public bool SetWindowPosResult { get; set; } = true;
        public int LastErrorCode { get; set; }

        public IntPtr SetWinEventHook(uint eventMin, uint eventMax, uint flags, WindowInterop.WinEventDelegate callback)
        {
            HookCount++;
            return new IntPtr(_nextHook++);
        }

        public bool UnhookWinEvent(IntPtr hookHandle) => true;

        public bool SetWindowPos(IntPtr windowHandle, IntPtr insertAfter, uint flags)
        {
            SetWindowPosCallCount++;
            return SetWindowPosResult;
        }
    }
}
