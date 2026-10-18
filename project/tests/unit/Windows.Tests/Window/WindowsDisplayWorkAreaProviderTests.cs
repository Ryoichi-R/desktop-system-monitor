using DesktopSystemMonitor.Core.Layout;
using DesktopSystemMonitor.Windows.Window;
using Xunit;

namespace DesktopSystemMonitor.Windows.Tests.Window;

public sealed class WindowsDisplayWorkAreaProviderTests
{
    [Fact]
    public void EnumerateDelegatesToMonitorEnumeratorAndReturnsAPrimaryMonitor()
    {
        var provider = new WindowsDisplayWorkAreaProvider();

        (IReadOnlyList<MonitorInfo> all, MonitorInfo primary) = provider.Enumerate();

        Assert.NotEmpty(all);
        Assert.Contains(all, monitor => monitor == primary);
        Assert.NotNull(primary.PhysicalWorkArea);
        Assert.Equal(primary.PhysicalWorkArea, primary.WorkArea);
        Assert.All(all, monitor =>
        {
            Assert.NotNull(monitor.PhysicalBounds);
            Assert.NotNull(monitor.EffectiveDpi);
            Assert.True(monitor.EffectiveDpi > 0);
            Assert.Equal(96, monitor.Dpi);
            Rect bounds = Assert.IsType<Rect>(monitor.PhysicalBounds);
            Assert.True(bounds.Width >= monitor.WorkArea.Width);
            Assert.True(bounds.Height >= monitor.WorkArea.Height);
        });
    }
}
