using DesktopSystemMonitor.Windows.Gpu;
using Xunit;

namespace DesktopSystemMonitor.Windows.Tests.Gpu;

[Trait("Category", "WindowsUnit")]
public sealed class GpuRecoveryTrackerTests
{
    [Fact]
    public void rebuild_is_requested_after_three_consecutive_collect_failures()
    {
        var tracker = new GpuRecoveryTracker();
        Assert.False(tracker.RecordCollectResult(false));
        Assert.False(tracker.RecordCollectResult(false));
        Assert.True(tracker.RecordCollectResult(false));
        Assert.False(tracker.RecordCollectResult(false));
    }

    [Fact]
    public void successful_collect_resets_failure_streak()
    {
        var tracker = new GpuRecoveryTracker();
        _ = tracker.RecordCollectResult(false);
        _ = tracker.RecordCollectResult(false);
        Assert.False(tracker.RecordCollectResult(true));
        Assert.False(tracker.RecordCollectResult(false));
    }

    [Fact]
    public void counters_with_only_invalid_values_trigger_rebuild()
    {
        var tracker = new GpuRecoveryTracker();
        Assert.False(tracker.RecordFormattedValues(4, 0));
        Assert.False(tracker.RecordFormattedValues(4, 0));
        Assert.True(tracker.RecordFormattedValues(4, 0));
        Assert.False(tracker.RecordFormattedValues(0, 0));
    }

    [Fact]
    public void valid_formatted_value_resets_failure_streak()
    {
        var tracker = new GpuRecoveryTracker();
        _ = tracker.RecordFormattedValues(2, 0);
        _ = tracker.RecordFormattedValues(2, 0);
        Assert.False(tracker.RecordFormattedValues(2, 1));
        Assert.False(tracker.RecordFormattedValues(2, 0));
    }
}
