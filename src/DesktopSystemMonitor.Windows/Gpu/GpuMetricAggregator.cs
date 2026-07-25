using System.Collections.Immutable;
using DesktopSystemMonitor.Core.Metrics;

namespace DesktopSystemMonitor.Windows.Gpu;

public readonly record struct GpuEngineReading(
    ulong Luid,
    uint PhysicalAdapter,
    uint EngineIndex,
    string EngineType,
    double UtilizationPercent);

public readonly record struct GpuMemoryReading(ulong Luid, long DedicatedUsageBytes);

/// <summary>Pure, testable GPU grouping and LUID-matching logic.</summary>
public static class GpuMetricAggregator
{
    public static GpuSnapshot Aggregate(
        IEnumerable<GpuEngineReading> engineReadings,
        IEnumerable<GpuMemoryReading> memoryReadings,
        IReadOnlyList<DxgiAdapterInfo> dxgiAdapters,
        ulong? preferredAdapterLuid = null)
    {
        ArgumentNullException.ThrowIfNull(engineReadings);
        ArgumentNullException.ThrowIfNull(memoryReadings);
        ArgumentNullException.ThrowIfNull(dxgiAdapters);

        var aggregates = new Dictionary<ulong, PerAdapterAggregate>();
        foreach (GpuEngineReading reading in engineReadings)
        {
            if (!double.IsFinite(reading.UtilizationPercent))
            {
                continue;
            }
            PerAdapterAggregate aggregate = GetOrCreate(aggregates, reading.Luid);
            var key = (reading.PhysicalAdapter, reading.EngineIndex, reading.EngineType);
            aggregate.EngineTotals.TryGetValue(key, out double existing);
            aggregate.EngineTotals[key] = existing + Math.Max(0, reading.UtilizationPercent);
        }
        foreach (GpuMemoryReading reading in memoryReadings)
        {
            if (reading.DedicatedUsageBytes < 0)
            {
                continue;
            }
            PerAdapterAggregate aggregate = GetOrCreate(aggregates, reading.Luid);
            aggregate.HasMemoryReading = true;
            aggregate.DedicatedUsage = checked(aggregate.DedicatedUsage + reading.DedicatedUsageBytes);
        }

        var dxgiByLuid = dxgiAdapters
            .Where(adapter => !adapter.IsSoftware)
            .GroupBy(adapter => adapter.Luid)
            .ToDictionary(group => group.Key, group => group.First());
        foreach (ulong luid in dxgiByLuid.Keys)
        {
            _ = GetOrCreate(aggregates, luid);
        }

        var snapshots = ImmutableArray.CreateBuilder<GpuAdapterSnapshot>();
        foreach ((ulong luid, PerAdapterAggregate aggregate) in aggregates)
        {
            dxgiByLuid.TryGetValue(luid, out DxgiAdapterInfo? dxgi);
            (double utilization, string engineType) = BusiestEngine(aggregate.EngineTotals);
            bool hasEngine = aggregate.EngineTotals.Count > 0;
            long limit = dxgi?.DedicatedVideoMemoryBytes ?? -1;
            MetricStatus memoryStatus = MemoryStatus(aggregate.HasMemoryReading, aggregate.DedicatedUsage, dxgi);

            snapshots.Add(new GpuAdapterSnapshot
            {
                Luid = luid,
                DisplayName = dxgi?.Description ?? $"GPU {luid:X}",
                UtilizationStatus = hasEngine ? MetricStatus.Ok : MetricStatus.Unavailable,
                UtilizationPercent = utilization,
                BusiestEngineType = hasEngine ? engineType : "Idle",
                MemoryStatus = memoryStatus,
                DedicatedUsageBytes = aggregate.DedicatedUsage,
                DedicatedLimitBytes = limit,
                IsIntegrated = dxgi?.DedicatedVideoMemoryBytes == 0,
            });
        }

        ImmutableArray<GpuAdapterSnapshot> sorted = snapshots
            .OrderByDescending(adapter => adapter.UtilizationStatus == MetricStatus.Ok)
            .ThenByDescending(adapter => adapter.UtilizationPercent)
            .ThenBy(adapter => adapter.Luid)
            .ToImmutableArray();
        return new GpuSnapshot
        {
            Adapters = sorted,
            OverallStatus = sorted.Length == 0 ? MetricStatus.Unavailable : MetricStatus.Ok,
            PreferredAdapterLuid = preferredAdapterLuid,
        };
    }

    private static MetricStatus MemoryStatus(bool hasReading, long usage, DxgiAdapterInfo? dxgi)
    {
        if (!hasReading || dxgi is null || usage < 0)
        {
            return MetricStatus.Unavailable;
        }
        long limit = dxgi.DedicatedVideoMemoryBytes;
        if (limit < 0 || (limit == 0 && usage != 0) || (limit > 0 && usage > limit))
        {
            return MetricStatus.Unavailable;
        }
        return MetricStatus.Ok;
    }

    private static (double Utilization, string EngineType) BusiestEngine(
        Dictionary<(uint PhysicalAdapter, uint EngineIndex, string EngineType), double> totals)
    {
        double busiest = 0;
        string type = "Idle";
        foreach (((uint _, uint _, string engineType), double total) in totals)
        {
            double clamped = Math.Clamp(total, 0, 100);
            if (clamped > busiest)
            {
                busiest = clamped;
                type = engineType;
            }
        }
        return (busiest, type);
    }

    private static PerAdapterAggregate GetOrCreate(Dictionary<ulong, PerAdapterAggregate> aggregates, ulong luid)
    {
        if (!aggregates.TryGetValue(luid, out PerAdapterAggregate? aggregate))
        {
            aggregate = new PerAdapterAggregate();
            aggregates.Add(luid, aggregate);
        }
        return aggregate;
    }

    private sealed class PerAdapterAggregate
    {
        public Dictionary<(uint PhysicalAdapter, uint EngineIndex, string EngineType), double> EngineTotals { get; } = new();
        public bool HasMemoryReading { get; set; }
        public long DedicatedUsage { get; set; }
    }
}
