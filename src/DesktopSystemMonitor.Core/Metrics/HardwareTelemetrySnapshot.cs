namespace DesktopSystemMonitor.Core.Metrics;

public sealed record HardwareTelemetrySnapshot
{
    public required PowerSnapshot Power { get; init; }
    public required TemperatureSnapshot Temperature { get; init; }

    public static HardwareTelemetrySnapshot Warmup() => new()
    {
        Power = PowerSnapshot.Warmup(),
        Temperature = TemperatureSnapshot.Warmup(),
    };

    public static HardwareTelemetrySnapshot Unavailable() => new()
    {
        Power = PowerSnapshot.Unavailable(),
        Temperature = TemperatureSnapshot.Unavailable(),
    };
}
