using System.Collections.Immutable;

namespace DesktopSystemMonitor.Core.Metrics;

public sealed record GpuAdapterSnapshot
{
    public required ulong Luid { get; init; }
    public required string DisplayName { get; init; }
    public required MetricStatus UtilizationStatus { get; init; }
    public required double UtilizationPercent { get; init; }
    public required string BusiestEngineType { get; init; }
    public required MetricStatus MemoryStatus { get; init; }
    public required long DedicatedUsageBytes { get; init; }
    public required long DedicatedLimitBytes { get; init; }
    public required bool IsIntegrated { get; init; }
}

public sealed record GpuSnapshot
{
    public required ImmutableArray<GpuAdapterSnapshot> Adapters { get; init; }
    public required MetricStatus OverallStatus { get; init; }
    public ulong? PreferredAdapterLuid { get; init; }

    public GpuAdapterSnapshot? PrimaryAdapter =>
        Adapters.IsDefaultOrEmpty ? null : Adapters[0];

    public GpuAdapterSnapshot? BusiestAdapter
    {
        get
        {
            if (Adapters.IsDefaultOrEmpty)
            {
                return null;
            }
            GpuAdapterSnapshot? busiest = null;
            foreach (var adapter in Adapters)
            {
                if (adapter.UtilizationStatus != MetricStatus.Ok)
                {
                    continue;
                }
                if (busiest is null || adapter.UtilizationPercent > busiest.UtilizationPercent)
                {
                    busiest = adapter;
                }
            }
            return busiest ?? Adapters[0];
        }
    }

    public GpuAdapterSnapshot? DisplayAdapter
    {
        get
        {
            if (PreferredAdapterLuid is ulong preferred)
            {
                foreach (GpuAdapterSnapshot adapter in Adapters)
                {
                    if (adapter.Luid == preferred)
                    {
                        return adapter;
                    }
                }
            }
            return BusiestAdapter;
        }
    }

    public static GpuSnapshot Warmup() => new()
    {
        Adapters = ImmutableArray<GpuAdapterSnapshot>.Empty,
        OverallStatus = MetricStatus.WarmingUp,
    };

    public static GpuSnapshot Unavailable() => new()
    {
        Adapters = ImmutableArray<GpuAdapterSnapshot>.Empty,
        OverallStatus = MetricStatus.Unavailable,
    };
}
