using System.Collections.Immutable;

namespace DesktopSystemMonitor.Core.Metrics;

public sealed record GpuPowerReading
{
    public required string DisplayName { get; init; }
    public required MetricStatus Status { get; init; }
    public required double Watts { get; init; }
}

public sealed record PowerSnapshot
{
    public required MetricStatus GpuCollectionStatus { get; init; }
    public required MetricStatus CpuPackageStatus { get; init; }
    public required double CpuPackageWatts { get; init; }
    public required ImmutableArray<GpuPowerReading> GpuReadings { get; init; }

    public static PowerSnapshot Warmup() => Missing(MetricStatus.WarmingUp);

    public static PowerSnapshot Unavailable() => Missing(MetricStatus.Unavailable);

    private static PowerSnapshot Missing(MetricStatus status) => new()
    {
        GpuCollectionStatus = status,
        CpuPackageStatus = status,
        CpuPackageWatts = double.NaN,
        GpuReadings = ImmutableArray<GpuPowerReading>.Empty,
    };
}
