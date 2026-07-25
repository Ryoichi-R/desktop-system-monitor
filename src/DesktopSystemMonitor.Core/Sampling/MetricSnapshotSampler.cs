using DesktopSystemMonitor.Core.Metrics;
using DesktopSystemMonitor.Core.Utility;

namespace DesktopSystemMonitor.Core.Sampling;

/// <summary>Samples each metric behind an independent failure boundary.</summary>
public sealed class MetricSnapshotSampler(
    IMetricSource<CpuSnapshot> cpu,
    IMetricSource<MemorySnapshot> memory,
    IMetricSource<GpuSnapshot> gpu,
    IMetricSource<NetworkSnapshot> network,
    IMetricSource<PowerSnapshot> power,
    IClock clock,
    Action<string, Exception> onError,
    IMetricSource<DiskSnapshot>? disk = null,
    IMetricSource<BatterySnapshot>? battery = null,
    IMetricSource<TemperatureSnapshot>? temperature = null)
{
    private int _baselineResetRequested;

    public MetricSnapshotSampler(
        IMetricSource<CpuSnapshot> cpu,
        IMetricSource<MemorySnapshot> memory,
        IMetricSource<GpuSnapshot> gpu,
        IMetricSource<NetworkSnapshot> network,
        IClock clock,
        Action<string, Exception> onError)
        : this(cpu, memory, gpu, network, new UnavailablePowerSource(), clock, onError)
    {
    }

    public async ValueTask<MetricSnapshot> SampleAsync(CancellationToken cancellationToken)
    {
        if (Interlocked.Exchange(ref _baselineResetRequested, 0) != 0)
        {
            ResetSource(cpu, "cpu", onError);
            ResetSource(memory, "memory", onError);
            ResetSource(gpu, "gpu", onError);
            ResetSource(network, "network", onError);
            ResetSource(power, "power", onError);
            if (disk is not null) ResetSource(disk, "disk", onError);
            if (battery is not null) ResetSource(battery, "battery", onError);
            if (temperature is not null) ResetSource(temperature, "temperature", onError);
        }

        CpuSnapshot cpuSnapshot = await SafeSampleAsync(
            cpu, CpuSnapshot.Unavailable, "cpu-sample-failure", onError, cancellationToken).ConfigureAwait(false);
        MemorySnapshot memorySnapshot = await SafeSampleAsync(
            memory, MemorySnapshot.Unavailable, "memory-sample-failure", onError, cancellationToken).ConfigureAwait(false);
        GpuSnapshot gpuSnapshot = await SafeSampleAsync(
            gpu, GpuSnapshot.Unavailable, "gpu-sample-failure", onError, cancellationToken).ConfigureAwait(false);
        NetworkSnapshot networkSnapshot = await SafeSampleAsync(
            network, NetworkSnapshot.Unavailable, "network-sample-failure", onError, cancellationToken).ConfigureAwait(false);
        PowerSnapshot powerSnapshot = await SafeSampleAsync(
            power, PowerSnapshot.Unavailable, "power-sample-failure", onError, cancellationToken).ConfigureAwait(false);
        DiskSnapshot diskSnapshot = disk is null ? DiskSnapshot.Unavailable() : await SafeSampleAsync(
            disk, () => DiskSnapshot.Unavailable(), "disk-sample-failure", onError, cancellationToken).ConfigureAwait(false);
        BatterySnapshot batterySnapshot = battery is null ? BatterySnapshot.Unavailable() : await SafeSampleAsync(
            battery, BatterySnapshot.Unavailable, "battery-sample-failure", onError, cancellationToken).ConfigureAwait(false);
        TemperatureSnapshot temperatureSnapshot = temperature is null ? TemperatureSnapshot.Unavailable() : await SafeSampleAsync(
            temperature, TemperatureSnapshot.Unavailable, "temperature-sample-failure", onError, cancellationToken).ConfigureAwait(false);
        return new MetricSnapshot
        {
            TakenAt = clock.UtcNow,
            Cpu = cpuSnapshot,
            Memory = memorySnapshot,
            Gpu = gpuSnapshot,
            Network = networkSnapshot,
            Power = powerSnapshot,
            Disk = diskSnapshot,
            Battery = batterySnapshot,
            Temperature = temperatureSnapshot,
        };
    }

    /// <summary>
    /// Requests an idempotent baseline reset. The reset is consumed immediately
    /// before a later sample, on the same execution path that owns sampling.
    /// </summary>
    public void RequestBaselineReset() => Interlocked.Exchange(ref _baselineResetRequested, 1);

    private static void ResetSource<T>(
        IMetricSource<T> source,
        string sourceName,
        Action<string, Exception> onError)
    {
        try
        {
            source.ResetBaseline();
        }
        catch (Exception ex)
        {
            onError($"{sourceName}-reset-failure", ex);
        }
    }

    private static async ValueTask<T> SafeSampleAsync<T>(
        IMetricSource<T> source,
        Func<T> unavailable,
        string sourceName,
        Action<string, Exception> onError,
        CancellationToken cancellationToken)
    {
        try
        {
            return await source.SampleAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            onError(sourceName, ex);
            return unavailable();
        }
    }

    private sealed class UnavailablePowerSource : IMetricSource<PowerSnapshot>
    {
        public ValueTask<PowerSnapshot> SampleAsync(CancellationToken cancellationToken) =>
            ValueTask.FromResult(PowerSnapshot.Unavailable());

        public void ResetBaseline()
        {
        }

        public void Dispose()
        {
        }
    }
}
