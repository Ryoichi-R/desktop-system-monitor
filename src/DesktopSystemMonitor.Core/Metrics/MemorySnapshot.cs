namespace DesktopSystemMonitor.Core.Metrics;

public sealed record MemorySnapshot
{
    public required MetricStatus Status { get; init; }
    public required double UtilizationPercent { get; init; }
    public required long UsedBytes { get; init; }
    public required long TotalBytes { get; init; }

    public static MemorySnapshot Warmup() => Missing(MetricStatus.WarmingUp);

    public static MemorySnapshot Unavailable() => Missing(MetricStatus.Unavailable);

    private static MemorySnapshot Missing(MetricStatus status) => new()
    {
        Status = status,
        UtilizationPercent = double.NaN,
        UsedBytes = -1,
        TotalBytes = -1,
    };
}
