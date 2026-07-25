namespace DesktopSystemMonitor.Core.Metrics;

public sealed record MetricSnapshot
{
    public required DateTimeOffset TakenAt { get; init; }
    public required CpuSnapshot Cpu { get; init; }
    public required MemorySnapshot Memory { get; init; }
    public required GpuSnapshot Gpu { get; init; }
    public required NetworkSnapshot Network { get; init; }
    public PowerSnapshot Power { get; init; } = PowerSnapshot.Warmup();
    public DiskSnapshot Disk { get; init; } = DiskSnapshot.Warmup();
    public BatterySnapshot Battery { get; init; } = BatterySnapshot.Warmup();
    public TemperatureSnapshot Temperature { get; init; } = TemperatureSnapshot.Warmup();

    public static MetricSnapshot Warmup(DateTimeOffset takenAt) => new()
    {
        TakenAt = takenAt,
        Cpu = CpuSnapshot.Warmup(),
        Memory = MemorySnapshot.Warmup(),
        Gpu = GpuSnapshot.Warmup(),
        Network = NetworkSnapshot.Warmup(),
        Power = PowerSnapshot.Warmup(),
        Disk = DiskSnapshot.Warmup(),
        Battery = BatterySnapshot.Warmup(),
        Temperature = TemperatureSnapshot.Warmup(),
    };
}
