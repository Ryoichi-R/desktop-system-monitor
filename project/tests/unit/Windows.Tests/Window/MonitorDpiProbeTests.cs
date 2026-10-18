using DesktopSystemMonitor.Core.Layout;
using DesktopSystemMonitor.Windows.Window;
using Xunit;

namespace DesktopSystemMonitor.Windows.Tests.Window;

public sealed class MonitorDpiProbeTests
{
    private static readonly Rect SecondaryBounds = new(-1920, 1084, 1920, 1080);
    private static readonly IntPtr ExpectedMonitor = new(66);

    [Theory]
    [InlineData(96u)]
    [InlineData(120u)]
    [InlineData(137u)]
    [InlineData(168u)]
    public void EffectiveDpiUsesTheMonitorWindowAndRestoresTheThreadContext(uint dpi)
    {
        var api = new FakeApi { Dpi = dpi };

        Assert.Equal((double)dpi, MonitorDpiProbe.ReadEffectiveDpi(SecondaryBounds, ExpectedMonitor, api));

        Assert.Equal(new[] { "context:-4", "create:-960,1624", "monitor", "dpi", "destroy", "context:-2" }, api.Calls);
    }

    [Fact]
    public void UnavailableContextDoesNotCreateAWindowOrGuess96Dpi()
    {
        var api = new FakeApi { PreviousContext = IntPtr.Zero };

        Assert.Null(MonitorDpiProbe.ReadEffectiveDpi(SecondaryBounds, ExpectedMonitor, api));
        Assert.Equal(new[] { "context:-4" }, api.Calls);
    }

    [Fact]
    public void FailedWindowCreationRestoresTheContextAndReportsUnavailable()
    {
        var api = new FakeApi { Window = IntPtr.Zero };

        Assert.Null(MonitorDpiProbe.ReadEffectiveDpi(SecondaryBounds, ExpectedMonitor, api));
        Assert.Equal(new[] { "context:-4", "create:-960,1624", "context:-2" }, api.Calls);
    }

    [Fact]
    public void ZeroDpiIsUnavailableAndTheWindowIsDestroyed()
    {
        var api = new FakeApi { Dpi = 0 };

        Assert.Null(MonitorDpiProbe.ReadEffectiveDpi(SecondaryBounds, ExpectedMonitor, api));
        Assert.Equal("destroy", api.Calls[^2]);
        Assert.Equal("context:-2", api.Calls[^1]);
    }

    [Fact]
    public void DpiQueryFailureStillDestroysTheWindowAndRestoresTheContext()
    {
        var api = new FakeApi { ThrowOnRead = true };

        Assert.Throws<InvalidOperationException>(() => MonitorDpiProbe.ReadEffectiveDpi(SecondaryBounds, ExpectedMonitor, api));
        Assert.Equal("destroy", api.Calls[^2]);
        Assert.Equal("context:-2", api.Calls[^1]);
    }

    [Theory]
    [InlineData(double.NaN, 0, 100, 100)]
    [InlineData(0, double.PositiveInfinity, 100, 100)]
    [InlineData(0, 0, 0, 100)]
    [InlineData(0, 0, 100, -1)]
    [InlineData(1e20, 0, 100, 100)]
    public void InvalidPhysicalBoundsDoNotCallNativeApis(double left, double top, double width, double height)
    {
        var api = new FakeApi();

        Assert.Null(MonitorDpiProbe.ReadEffectiveDpi(new Rect(left, top, width, height), ExpectedMonitor, api));
        Assert.Empty(api.Calls);
    }

    [Fact]
    public void ChangedMonitorTopologyDoesNotAttributeAnotherMonitorsDpi()
    {
        var api = new FakeApi { Monitor = new IntPtr(99) };

        Assert.Null(MonitorDpiProbe.ReadEffectiveDpi(SecondaryBounds, ExpectedMonitor, api));
        Assert.DoesNotContain("dpi", api.Calls);
        Assert.Equal("destroy", api.Calls[^2]);
        Assert.Equal("context:-2", api.Calls[^1]);
    }

    private sealed class FakeApi : IMonitorDpiProbeApi
    {
        public uint Dpi { get; init; } = 168;
        public IntPtr PreviousContext { get; init; } = new(-2);
        public IntPtr Window { get; init; } = new(42);
        public IntPtr Monitor { get; init; } = ExpectedMonitor;
        public bool ThrowOnRead { get; init; }
        public List<string> Calls { get; } = [];

        public IntPtr SetThreadContext(IntPtr context)
        {
            Calls.Add("context:" + context.ToInt64());
            return PreviousContext;
        }

        public IntPtr CreateHiddenWindow(int x, int y)
        {
            Calls.Add($"create:{x},{y}");
            return Window;
        }

        public uint GetWindowDpi(IntPtr window)
        {
            Calls.Add("dpi");
            if (ThrowOnRead)
            {
                throw new InvalidOperationException("fake query failure");
            }
            return Dpi;
        }

        public IntPtr GetWindowMonitor(IntPtr window)
        {
            Calls.Add("monitor");
            return Monitor;
        }

        public void DestroyWindow(IntPtr window) => Calls.Add("destroy");
    }
}
