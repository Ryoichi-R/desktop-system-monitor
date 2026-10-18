using System.Collections.Immutable;
using DesktopSystemMonitor.Core.Metrics;
using Xunit;

namespace DesktopSystemMonitor.Core.Tests.Metrics;

public class GpuSnapshotTests
{
    private static GpuAdapterSnapshot MakeAdapter(ulong luid, double util, MetricStatus status = MetricStatus.Ok) => new()
    {
        Luid = luid,
        DisplayName = "GPU " + luid,
        UtilizationStatus = status,
        UtilizationPercent = util,
        BusiestEngineType = "3D",
        MemoryStatus = MetricStatus.Ok,
        DedicatedUsageBytes = 0,
        DedicatedLimitBytes = 0,
        IsIntegrated = false,
    };

    [Fact]
    public void busiest_adapter_picks_highest_utilization()
    {
        var snap = new GpuSnapshot
        {
            Adapters = ImmutableArray.Create(
                MakeAdapter(1, 12),
                MakeAdapter(2, 87),
                MakeAdapter(3, 44)),
            OverallStatus = MetricStatus.Ok,
        };
        Assert.Equal(2ul, snap.BusiestAdapter!.Luid);
    }

    [Fact]
    public void busiest_adapter_ignores_warmup_entries()
    {
        var snap = new GpuSnapshot
        {
            Adapters = ImmutableArray.Create(
                MakeAdapter(1, 99, MetricStatus.WarmingUp),
                MakeAdapter(2, 20)),
            OverallStatus = MetricStatus.Ok,
        };
        Assert.Equal(2ul, snap.BusiestAdapter!.Luid);
    }

    [Fact]
    public void busiest_adapter_is_null_when_empty()
    {
        var snap = GpuSnapshot.Warmup();
        Assert.Null(snap.BusiestAdapter);
    }

    [Fact]
    public void display_adapter_honors_preferred_luid_even_when_it_is_not_busiest()
    {
        var snap = new GpuSnapshot
        {
            Adapters = ImmutableArray.Create(MakeAdapter(1, 90), MakeAdapter(2, 10)),
            OverallStatus = MetricStatus.Ok,
            PreferredAdapterLuid = 2,
        };
        Assert.Equal(2UL, snap.DisplayAdapter!.Luid);
    }

    [Fact]
    public void display_adapter_falls_back_to_busiest_when_preferred_is_missing()
    {
        var snap = new GpuSnapshot
        {
            Adapters = ImmutableArray.Create(MakeAdapter(1, 90), MakeAdapter(2, 10)),
            OverallStatus = MetricStatus.Ok,
            PreferredAdapterLuid = 99,
        };
        Assert.Equal(1UL, snap.DisplayAdapter!.Luid);
    }
}
