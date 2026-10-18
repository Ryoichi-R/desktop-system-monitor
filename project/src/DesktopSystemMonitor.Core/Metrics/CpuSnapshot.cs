namespace DesktopSystemMonitor.Core.Metrics;

public sealed record CpuSnapshot
{
    public required MetricStatus UtilizationStatus { get; init; }
    public required double UtilizationPercent { get; init; }
    public required MetricStatus FrequencyStatus { get; init; }
    public required double FrequencyMhz { get; init; }
    public required bool FrequencyIsEstimate { get; init; }
    public required double? RawUtilizationPercent { get; init; }

    public static CpuSnapshot Warmup() => new()
    {
        UtilizationStatus = MetricStatus.WarmingUp,
        UtilizationPercent = double.NaN,
        FrequencyStatus = MetricStatus.WarmingUp,
        FrequencyMhz = double.NaN,
        FrequencyIsEstimate = true,
        RawUtilizationPercent = null,
    };

    public static CpuSnapshot Unavailable() => new()
    {
        UtilizationStatus = MetricStatus.Unavailable,
        UtilizationPercent = double.NaN,
        FrequencyStatus = MetricStatus.Unavailable,
        FrequencyMhz = double.NaN,
        FrequencyIsEstimate = true,
        RawUtilizationPercent = null,
    };
}
