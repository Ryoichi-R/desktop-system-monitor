using DesktopSystemMonitor.Core.Utility;
using DesktopSystemMonitor.Windows.Gpu;
using Xunit;

namespace DesktopSystemMonitor.Windows.Tests.Gpu;

public sealed class GpuMetricSourceClockTests
{
    [Fact]
    public async Task wildcard_refresh_uses_injected_clock_boundary()
    {
        DateTimeOffset start = new(2026, 7, 12, 0, 0, 0, TimeSpan.Zero);
        var clock = new FakeClock(start);
        using var source = new GpuMetricSource(
            TimeSpan.FromSeconds(5),
            [new DxgiAdapterInfo(1, "Test GPU", 1, 0, 0, false)],
            clock);
        Assert.Equal(1, source.WildcardRefreshCount);

        clock.UtcNow = start.AddTicks(TimeSpan.FromSeconds(5).Ticks - 1);
        _ = await source.SampleAsync(default);
        Assert.Equal(1, source.WildcardRefreshCount);

        clock.UtcNow = start.AddSeconds(5);
        _ = await source.SampleAsync(default);
        Assert.Equal(2, source.WildcardRefreshCount);
    }

    private sealed class FakeClock(DateTimeOffset utcNow) : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = utcNow;
        public long GetTimestampTicks() => 0;
        public double TicksToSeconds(long deltaTicks) => 0;
    }
}
