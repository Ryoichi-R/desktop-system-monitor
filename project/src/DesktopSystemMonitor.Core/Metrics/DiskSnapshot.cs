namespace DesktopSystemMonitor.Core.Metrics;

public enum DiskAvailabilityReason
{
    None,
    SystemDiskResolveFailed,
    SystemVolumeSpansMultipleDisks,
    RequestedDiskNotFound,
    PdhEnumerationFailed,
    CounterOpenFailed,
    CollectionFailed,
    CounterReadFailed,
}

public sealed record DiskSnapshot
{
    public required MetricStatus Status { get; init; }
    public required double ActivePercent { get; init; }
    public required double ReadBytesPerSecond { get; init; }
    public required double WriteBytesPerSecond { get; init; }
    public required int? SelectedDiskNumber { get; init; }
    public required string SelectedDiskLabel { get; init; }
    public DiskAvailabilityReason AvailabilityReason { get; init; }

    public static DiskSnapshot Warmup() => Missing(MetricStatus.WarmingUp, "--");
    public static DiskSnapshot Unavailable(string label = "N/A", DiskAvailabilityReason reason = DiskAvailabilityReason.CollectionFailed) =>
        Missing(MetricStatus.Unavailable, label) with { AvailabilityReason = reason };

    private static DiskSnapshot Missing(MetricStatus status, string label) => new()
    {
        Status = status,
        ActivePercent = double.NaN,
        ReadBytesPerSecond = double.NaN,
        WriteBytesPerSecond = double.NaN,
        SelectedDiskNumber = null,
        SelectedDiskLabel = label,
    };
}
