using DesktopSystemMonitor.Core.Platform;
using DesktopSystemMonitor.Mac;
using DesktopSystemMonitor.Mac.Interop;
using Xunit;

namespace DesktopSystemMonitor.Mac.Tests;

public sealed class MacWidgetLayerControllerTests
{
    [Fact]
    public void Applies_Collection_Behavior_Layer_And_Click_Through()
    {
        var interop = new FakeInterop();
        using var controller = new MacWidgetLayerController(interop);
        controller.Attach(new IntPtr(42));
        controller.SetLayerMode(WidgetLayerMode.AlwaysOnTop);
        controller.SetClickThrough(true);

        Assert.Equal(1, interop.CollectionBehaviorCalls);
        Assert.Equal(interop.FloatingWindowLevel, interop.Level);
        Assert.True(interop.IgnoresMouseEvents);
        Assert.True(controller.TryRecoverLayer());
        Assert.False(controller.Health.IsDegraded);
    }

    [Fact]
    public void Native_Failure_Is_Reported_Without_Escaping()
    {
        var interop = new FakeInterop { ThrowOnLevel = true };
        using var controller = new MacWidgetLayerController(interop);
        var health = new List<WidgetLayerHealth>();
        controller.HealthChanged += health.Add;
        controller.Attach(new IntPtr(42));
        controller.SetLayerMode(WidgetLayerMode.AlwaysOnBottom);

        Assert.True(controller.Health.IsDegraded);
        Assert.Equal("setLevel", controller.Health.Operation);
        Assert.NotEmpty(health);
        Assert.False(controller.TryRecoverLayer());
    }

    [Fact]
    public void Requires_Attach_And_Dispose_Is_Stable()
    {
        using var controller = new MacWidgetLayerController(new FakeInterop());
        Assert.Throws<InvalidOperationException>(() => controller.SetClickThrough(true));
        controller.Dispose();
        controller.Dispose();
        Assert.Throws<ObjectDisposedException>(() => controller.TryRecoverLayer());
    }

    private sealed class FakeInterop : IMacWindowInterop
    {
        public int DesktopWindowLevel => -100;
        public int NormalWindowLevel => 0;
        public int FloatingWindowLevel => 3;
        public int Level { get; private set; }
        public bool IgnoresMouseEvents { get; private set; }
        public int CollectionBehaviorCalls { get; private set; }
        public bool ThrowOnLevel { get; init; }

        public void SetLevel(IntPtr window, int level)
        {
            if (ThrowOnLevel) throw new InvalidOperationException("test");
            Level = level;
        }

        public int GetLevel(IntPtr window) => Level;
        public void SetIgnoresMouseEvents(IntPtr window, bool enabled) => IgnoresMouseEvents = enabled;
        public bool GetIgnoresMouseEvents(IntPtr window) => IgnoresMouseEvents;
        public void SetCollectionBehavior(IntPtr window, nuint behavior) => CollectionBehaviorCalls++;
    }
}
