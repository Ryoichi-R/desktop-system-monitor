using DesktopSystemMonitor.Windows.Pdh;
using Xunit;

namespace DesktopSystemMonitor.Windows.Tests.Pdh;

public sealed class PdhQueryTests
{
    [Fact]
    public void bool_apis_return_false_after_dispose()
    {
        var query = new PdhQuery();
        query.Dispose();

        Assert.False(query.Collect());
        Assert.False(query.TryAddCounter(@"\Processor(_Total)\% Processor Time"));
        Assert.False(query.TryGetDouble(@"\Processor(_Total)\% Processor Time", out double value));
        Assert.True(double.IsNaN(value));
        Assert.False(query.RemoveCounter(@"\Processor(_Total)\% Processor Time"));

        query.Dispose();
    }
}
