namespace DesktopSystemMonitor.Core.Metrics;

public enum BatteryPowerState
{
    Absent,
    WarmingUp,
    Discharging,
    Charging,
    AcConnected,
    Unknown,
}

public sealed record BatterySnapshot
{
    public required MetricStatus Status { get; init; }
    public required BatteryPowerState PowerState { get; init; }
    public required bool BatteryPresent { get; init; }
    public required double Percent { get; init; }
    public required double RemainingCapacityMilliwattHours { get; init; }
    public required double RateMilliwatts { get; init; }
    public required TimeSpan? WindowsEstimatedTime { get; init; }

    public static BatterySnapshot Warmup() => Missing(MetricStatus.WarmingUp, BatteryPowerState.WarmingUp);
    public static BatterySnapshot Unavailable() => Missing(MetricStatus.Unavailable, BatteryPowerState.Unknown);
    public static BatterySnapshot Absent() => Missing(MetricStatus.Unavailable, BatteryPowerState.Absent);

    private static BatterySnapshot Missing(MetricStatus status, BatteryPowerState state) => new()
    {
        Status = status,
        PowerState = state,
        BatteryPresent = false,
        Percent = double.NaN,
        RemainingCapacityMilliwattHours = double.NaN,
        RateMilliwatts = double.NaN,
        WindowsEstimatedTime = null,
    };
}
