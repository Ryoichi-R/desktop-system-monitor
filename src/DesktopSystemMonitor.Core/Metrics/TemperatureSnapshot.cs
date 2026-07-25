using System.Collections.Immutable;

namespace DesktopSystemMonitor.Core.Metrics;

public sealed record GpuTemperatureReading
{
    public required string DisplayName { get; init; }
    public required MetricStatus Status { get; init; }
    public required double Celsius { get; init; }
}

public sealed record TemperatureSnapshot
{
    public required MetricStatus CpuPackageStatus { get; init; }
    public required double CpuPackageCelsius { get; init; }
    public required MetricStatus GpuCollectionStatus { get; init; }
    public required ImmutableArray<GpuTemperatureReading> GpuReadings { get; init; }

    public static TemperatureSnapshot Warmup() => Missing(MetricStatus.WarmingUp);
    public static TemperatureSnapshot Unavailable() => Missing(MetricStatus.Unavailable);

    private static TemperatureSnapshot Missing(MetricStatus status) => new()
    {
        CpuPackageStatus = status,
        CpuPackageCelsius = double.NaN,
        GpuCollectionStatus = status,
        GpuReadings = ImmutableArray<GpuTemperatureReading>.Empty,
    };
}
