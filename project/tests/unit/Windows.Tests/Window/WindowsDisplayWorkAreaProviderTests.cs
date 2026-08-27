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
    }
}
