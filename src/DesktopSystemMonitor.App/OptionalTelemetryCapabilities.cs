using DesktopSystemMonitor.Core.Metrics;

namespace DesktopSystemMonitor.App;

internal enum OptionalTelemetryCapabilityStatus
{
    Supported,
    NotDetected,
    Unavailable,
}

internal sealed record OptionalTelemetryCapabilities(
    OptionalTelemetryCapabilityStatus CpuTemperature,
    OptionalTelemetryCapabilityStatus GpuTemperature,
    OptionalTelemetryCapabilityStatus Battery)
{
    public static OptionalTelemetryCapabilities Unknown { get; } = new(
        OptionalTelemetryCapabilityStatus.NotDetected,
        OptionalTelemetryCapabilityStatus.NotDetected,
        OptionalTelemetryCapabilityStatus.NotDetected);

    public static OptionalTelemetryCapabilities FromSnapshot(MetricSnapshot? snapshot)
    {
        if (snapshot is null)
        {
            return Unknown;
        }

        OptionalTelemetryCapabilityStatus cpuTemperature = FromMetricStatus(snapshot.Temperature.CpuPackageStatus);
        OptionalTelemetryCapabilityStatus gpuTemperature = FromMetricStatus(snapshot.Temperature.GpuCollectionStatus);

        OptionalTelemetryCapabilityStatus battery = snapshot.Battery.PowerState switch
        {
            BatteryPowerState.Absent => OptionalTelemetryCapabilityStatus.NotDetected,
            _ when snapshot.Battery.BatteryPresent && snapshot.Battery.Status == MetricStatus.Ok =>
                OptionalTelemetryCapabilityStatus.Supported,
            _ => OptionalTelemetryCapabilityStatus.Unavailable,
        };

        return new OptionalTelemetryCapabilities(cpuTemperature, gpuTemperature, battery);
    }

    private static OptionalTelemetryCapabilityStatus FromMetricStatus(MetricStatus status) => status switch
    {
        MetricStatus.Ok => OptionalTelemetryCapabilityStatus.Supported,
        MetricStatus.WarmingUp => OptionalTelemetryCapabilityStatus.NotDetected,
        _ => OptionalTelemetryCapabilityStatus.Unavailable,
    };

    public static string Display(OptionalTelemetryCapabilityStatus status) => status switch
    {
        OptionalTelemetryCapabilityStatus.Supported => "対応",
        OptionalTelemetryCapabilityStatus.NotDetected => "未検出",
        _ => "取得不可",
    };
}
