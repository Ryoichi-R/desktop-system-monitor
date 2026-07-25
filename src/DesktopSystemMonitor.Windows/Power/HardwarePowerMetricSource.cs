using System.Collections.Immutable;
using System.Runtime.InteropServices;
using DesktopSystemMonitor.Core.Metrics;
using LibreHardwareMonitor.Hardware;

namespace DesktopSystemMonitor.Windows.Power;

/// <summary>
/// Polls CPU package and GPU package/board power on a dedicated worker. The
/// application sampling loop only reads the latest immutable snapshot, so a
/// slow hardware driver cannot delay the basic CPU/GPU/network metrics.
/// </summary>
public interface IPowerMetricSource : IMetricSource<PowerSnapshot>
{
    void PausePolling();
    void ResumePolling();
}

public sealed class HardwarePowerMetricSource : IPowerMetricSource, IMetricSource<TemperatureSnapshot>
{
    private static readonly TimeSpan DefaultInterval = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan DefaultDisposeWait = TimeSpan.FromSeconds(2);

    private readonly CancellationTokenSource _stop = new();
    private readonly AutoResetEvent _wake = new(false);
    private readonly Task _worker;
    private readonly TimeSpan _disposeWait;
    private PowerSnapshot _latest = PowerSnapshot.Warmup();
    private TemperatureSnapshot _latestTemperature = TemperatureSnapshot.Warmup();
    private int _disposed;
    private int _paused;
    private int _workerResourcesDisposed;

    public HardwarePowerMetricSource(Action<string, Exception>? onError = null)
        : this(
            () => new LibreHardwarePowerSession(onError),
            DefaultInterval,
            DefaultDisposeWait,
            onError)
    {
    }

    internal HardwarePowerMetricSource(
        Func<IHardwarePowerSession> sessionFactory,
        TimeSpan interval,
        TimeSpan disposeWait,
        Action<string, Exception>? onError = null)
    {
        ArgumentNullException.ThrowIfNull(sessionFactory);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(interval, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfLessThan(disposeWait, TimeSpan.Zero);

        _disposeWait = disposeWait;
        _worker = Task.Factory.StartNew(
            () => RunWorker(sessionFactory, interval, onError),
            CancellationToken.None,
            TaskCreationOptions.LongRunning,
            TaskScheduler.Default);
    }

    public ValueTask<PowerSnapshot> SampleAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        return ValueTask.FromResult(Volatile.Read(ref _latest));
    }

    ValueTask<TemperatureSnapshot> IMetricSource<TemperatureSnapshot>.SampleAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        return ValueTask.FromResult(Volatile.Read(ref _latestTemperature));
    }

    public void ResetBaseline()
    {
        // Absolute power readings do not use a delta baseline.
    }

    public void PausePolling()
    {
        if (Volatile.Read(ref _disposed) != 0)
        {
            return;
        }
        Interlocked.Exchange(ref _paused, 1);
        SignalWorker();
    }

    public void ResumePolling()
    {
        if (Volatile.Read(ref _disposed) != 0)
        {
            return;
        }
        if (_worker.IsCompleted)
        {
            return;
        }
        Volatile.Write(ref _latest, PowerSnapshot.Warmup());
        Volatile.Write(ref _latestTemperature, TemperatureSnapshot.Warmup());
        Interlocked.Exchange(ref _paused, 0);
        SignalWorker();
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        _stop.Cancel();
        SignalWorker();
        try
        {
            _ = _worker.Wait(_disposeWait);
        }
        catch (AggregateException)
        {
            // The worker reports its own failures and the process is exiting.
        }

        if (_worker.IsCompleted)
        {
            DisposeWorkerResources();
        }
        else
        {
            _ = _worker.ContinueWith(
                _ => DisposeWorkerResources(),
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
        }
    }

    private void RunWorker(
        Func<IHardwarePowerSession> sessionFactory,
        TimeSpan interval,
        Action<string, Exception>? onError)
    {
        IHardwarePowerSession? session = null;
        try
        {
            session = sessionFactory();
            WaitHandle[] waits = [_stop.Token.WaitHandle, _wake];
            while (!_stop.IsCancellationRequested)
            {
                if (Volatile.Read(ref _paused) != 0)
                {
                    if (WaitHandle.WaitAny(waits) == 0)
                    {
                        break;
                    }
                    continue;
                }

                try
                {
                    Volatile.Write(ref _latest, session.Sample());
                    Volatile.Write(ref _latestTemperature, session.Temperature);
                }
                catch (Exception ex)
                {
                    Report(onError, "power-sample-failure", ex);
                    Volatile.Write(ref _latest, PowerSnapshot.Unavailable());
                    Volatile.Write(ref _latestTemperature, TemperatureSnapshot.Unavailable());
                }

                if (WaitHandle.WaitAny(waits, interval) == 0)
                {
                    break;
                }
            }
        }
        catch (Exception ex)
        {
            Report(onError, "power-initialization-failure", ex);
            Volatile.Write(ref _latest, PowerSnapshot.Unavailable());
            Volatile.Write(ref _latestTemperature, TemperatureSnapshot.Unavailable());
        }
        finally
        {
            try
            {
                session?.Dispose();
            }
            catch (Exception ex)
            {
                Report(onError, "power-dispose-failure", ex);
            }
        }
    }

    private void DisposeWorkerResources()
    {
        if (Interlocked.Exchange(ref _workerResourcesDisposed, 1) != 0)
        {
            return;
        }
        _wake.Dispose();
        _stop.Dispose();
    }

    private void SignalWorker()
    {
        try
        {
            _wake.Set();
        }
        catch (ObjectDisposedException)
        {
            // A concurrent Dispose completed after the caller's state check.
        }
    }

    internal static void Report(Action<string, Exception>? onError, string stage, Exception exception)
    {
        try
        {
            onError?.Invoke(stage, exception);
        }
        catch
        {
            // Diagnostics must never terminate the sensor worker.
        }
    }
}

internal interface IHardwarePowerSession : IDisposable
{
    PowerSnapshot Sample();
    TemperatureSnapshot Temperature => TemperatureSnapshot.Unavailable();
}

internal sealed class LibreHardwarePowerSession : IHardwarePowerSession
{
    private static readonly TimeSpan CpuPowerInitializationRetryInterval = TimeSpan.FromSeconds(30);

    private readonly Computer? _computer;
    private readonly RetryingCpuPowerReader _cpuPowerReader;
    private readonly Action<string, Exception>? _onError;
    public TemperatureSnapshot Temperature { get; private set; } = TemperatureSnapshot.Warmup();

    public LibreHardwarePowerSession(Action<string, Exception>? onError)
        : this(
            onError,
            RuntimeInformation.OSArchitecture,
            () => EnergyMeterCpuPowerReader.TryCreate(onError))
    {
    }

    internal LibreHardwarePowerSession(
        Action<string, Exception>? onError,
        Architecture osArchitecture,
        Func<ICpuPowerReader> cpuPowerReaderFactory)
    {
        ArgumentNullException.ThrowIfNull(cpuPowerReaderFactory);
        _onError = onError;

        // LibreHardwareMonitor supplies supported GPU power and the fallback CPU
        // package reading. Version 0.9.6 does not support Qualcomm Adreno, and its
        // low-level initialization can raise a native SEHException on Snapdragon.
        // Skip it before Open() on ARM64 so no partially initialized native state is
        // left behind; the independent Energy Meter CPU path remains available.
        _computer = OpenComputer(osArchitecture, onError);
        _cpuPowerReader = new RetryingCpuPowerReader(
            cpuPowerReaderFactory,
            CpuPowerInitializationRetryInterval);
    }

    internal static bool ShouldOpenLibreHardwareMonitor(Architecture osArchitecture) =>
        osArchitecture != Architecture.Arm64;

    private static Computer? OpenComputer(
        Architecture osArchitecture,
        Action<string, Exception>? onError)
    {
        if (!ShouldOpenLibreHardwareMonitor(osArchitecture))
        {
            return null;
        }

        var computer = new Computer
        {
            IsCpuEnabled = true,
            IsGpuEnabled = true,
        };
        OpenWithCleanup(
            computer,
            static value => value.Open(),
            static value => value.Close(),
            onError);
        return computer;
    }

    internal static void OpenWithCleanup(
        Computer computer,
        Action<Computer> openComputer,
        Action<Computer> closeComputer,
        Action<string, Exception>? onError)
    {
        ArgumentNullException.ThrowIfNull(computer);
        ArgumentNullException.ThrowIfNull(openComputer);
        ArgumentNullException.ThrowIfNull(closeComputer);

        try
        {
            openComputer(computer);
        }
        catch
        {
            try
            {
                closeComputer(computer);
            }
            catch (Exception ex)
            {
                HardwarePowerMetricSource.Report(
                    onError,
                    "power-initialization-cleanup-failure",
                    ex);
            }
            throw;
        }
    }

    public PowerSnapshot Sample()
    {
        var readings = new List<PowerSensorReading>();
        var temperatures = new List<TemperatureSensorReading>();
        if (_computer is not null)
        {
            foreach (IHardware hardware in _computer.Hardware)
            {
                CollectReadings(hardware, readings, temperatures);
            }
        }
        PowerSnapshot hardwareSnapshot = PowerSensorSelector.Select(readings);
        Temperature = TemperatureSensorSelector.Select(temperatures);
        return PowerSnapshotCpuPreference.Apply(hardwareSnapshot, _cpuPowerReader.Sample());
    }

    public void Dispose()
    {
        try
        {
            _cpuPowerReader.Dispose();
        }
        catch (Exception ex)
        {
            HardwarePowerMetricSource.Report(_onError, "cpu-energy-meter-dispose-failure", ex);
        }
        _computer?.Close();
    }

    private void CollectReadings(IHardware hardware, List<PowerSensorReading> readings, List<TemperatureSensorReading> temperatures)
    {
        if (PowerSensorSelector.IsSupportedHardware(hardware.HardwareType))
        {
            try
            {
                hardware.Update();
                foreach (ISensor sensor in hardware.Sensors)
                {
                    if (sensor.SensorType == SensorType.Power)
                    {
                        readings.Add(new PowerSensorReading(
                            hardware.HardwareType,
                            hardware.Identifier.ToString(),
                            hardware.Name,
                            sensor.Name,
                            sensor.Value));
                    }
                    else if (sensor.SensorType == SensorType.Temperature)
                    {
                        temperatures.Add(new TemperatureSensorReading(
                            hardware.HardwareType,
                            hardware.Identifier.ToString(),
                            hardware.Name,
                            sensor.Name,
                            sensor.Value));
                    }
                }
            }
            catch (Exception ex)
            {
                HardwarePowerMetricSource.Report(_onError, "power-device-sample-failure", ex);
                readings.Add(new PowerSensorReading(
                    hardware.HardwareType,
                    hardware.Identifier.ToString(),
                    hardware.Name,
                    string.Empty,
                    null));
                temperatures.Add(new TemperatureSensorReading(
                    hardware.HardwareType,
                    hardware.Identifier.ToString(),
                    hardware.Name,
                    string.Empty,
                    null));
            }
        }

        try
        {
            foreach (IHardware child in hardware.SubHardware)
            {
                CollectReadings(child, readings, temperatures);
            }
        }
        catch (Exception ex)
        {
            HardwarePowerMetricSource.Report(_onError, "power-subhardware-sample-failure", ex);
        }
    }
}

internal readonly record struct PowerSensorReading(
    HardwareType HardwareType,
    string HardwareIdentifier,
    string HardwareName,
    string SensorName,
    float? Watts);

internal readonly record struct TemperatureSensorReading(
    HardwareType HardwareType,
    string HardwareIdentifier,
    string HardwareName,
    string SensorName,
    float? Celsius);

internal static class TemperatureSensorSelector
{
    internal static TemperatureSnapshot Select(IEnumerable<TemperatureSensorReading> readings)
    {
        TemperatureSensorReading[] all = readings.ToArray();
        double[] cpu = all.Where(r => r.HardwareType == HardwareType.Cpu)
            .GroupBy(r => r.HardwareIdentifier, StringComparer.Ordinal)
            .Select(SelectCpu)
            .Where(value => value is not null)
            .Select(value => value!.Value)
            .ToArray();
        ImmutableArray<GpuTemperatureReading> gpu = all.Where(r => PowerSensorSelector.IsSupportedHardware(r.HardwareType) && r.HardwareType != HardwareType.Cpu)
            .GroupBy(r => r.HardwareIdentifier, StringComparer.Ordinal)
            .Select(group =>
            {
                double? value = group.Where(r => r.SensorName.Equals("GPU Core", StringComparison.OrdinalIgnoreCase)).Select(Valid).FirstOrDefault(v => v is not null);
                return new GpuTemperatureReading { DisplayName = group.First().HardwareName, Status = value is null ? MetricStatus.Unavailable : MetricStatus.Ok, Celsius = value ?? double.NaN };
            }).ToImmutableArray();
        return new TemperatureSnapshot
        {
            CpuPackageStatus = cpu.Length == 0 ? MetricStatus.Unavailable : MetricStatus.Ok,
            CpuPackageCelsius = cpu.Length == 0 ? double.NaN : cpu.Max(),
            GpuCollectionStatus = gpu.Length == 0 ? MetricStatus.Unavailable : MetricStatus.Ok,
            GpuReadings = gpu,
        };
    }

    private static double? SelectCpu(IEnumerable<TemperatureSensorReading> readings)
    {
        TemperatureSensorReading[] values = readings.ToArray();
        foreach (string exact in new[] { "CPU Package", "Package", "Core Average" })
        {
            double? match = values.Where(r => r.SensorName.Equals(exact, StringComparison.OrdinalIgnoreCase)).Select(Valid).FirstOrDefault(v => v is not null);
            if (match is not null) return match;
        }
        double[] cores = values.Where(r => r.SensorName.Contains("Core", StringComparison.OrdinalIgnoreCase)).Select(Valid).Where(v => v is not null).Select(v => v!.Value).ToArray();
        return cores.Length == 0 ? null : cores.Max();
    }

    private static double? Valid(TemperatureSensorReading reading) =>
        reading.Celsius is float value && float.IsFinite(value) && value is >= -20 and <= 150 ? value : null;
}

internal static class PowerSensorSelector
{
    internal const float MaximumReasonableWatts = 5_000f;

    public static PowerSnapshot Select(IEnumerable<PowerSensorReading> readings)
    {
        PowerSensorReading[] all = readings.ToArray();
        double[] cpuPackages = all
            .Where(reading => reading.HardwareType == HardwareType.Cpu)
            .GroupBy(reading => reading.HardwareIdentifier, StringComparer.Ordinal)
            .Select(group => SelectWatts(group, CpuPriority, allowZero: false))
            .Where(watts => watts is not null)
            .Select(watts => watts!.Value)
            .ToArray();

        ImmutableArray<GpuPowerReading> gpu = all
            .Where(reading => IsGpu(reading.HardwareType))
            .GroupBy(reading => reading.HardwareIdentifier, StringComparer.Ordinal)
            .Select(group =>
            {
                double? watts = SelectWatts(group, GpuPriority, allowZero: true);
                return new GpuPowerReading
                {
                    DisplayName = group.First().HardwareName,
                    Status = watts is null ? MetricStatus.Unavailable : MetricStatus.Ok,
                    Watts = watts ?? double.NaN,
                };
            })
            .ToImmutableArray();

        return new PowerSnapshot
        {
            GpuCollectionStatus = gpu.Length > 0 ? MetricStatus.Ok : MetricStatus.Unavailable,
            CpuPackageStatus = cpuPackages.Length == 0 ? MetricStatus.Unavailable : MetricStatus.Ok,
            CpuPackageWatts = cpuPackages.Length == 0 ? double.NaN : cpuPackages.Sum(),
            GpuReadings = gpu,
        };
    }

    internal static bool IsSupportedHardware(HardwareType type) =>
        type == HardwareType.Cpu || IsGpu(type);

    private static double? SelectWatts(
        IEnumerable<PowerSensorReading> readings,
        Func<string, int> priority,
        bool allowZero)
    {
        return readings
            .Where(reading => reading.Watts is float watts
                && float.IsFinite(watts)
                && (allowZero ? watts >= 0 : watts > 0)
                && watts <= MaximumReasonableWatts)
            .Select(reading => new { Reading = reading, Priority = priority(reading.SensorName) })
            .Where(item => item.Priority < int.MaxValue)
            .OrderBy(item => item.Priority)
            .ThenBy(item => item.Reading.SensorName, StringComparer.OrdinalIgnoreCase)
            .Select(item => (double?)item.Reading.Watts!.Value)
            .FirstOrDefault();
    }

    private static int CpuPriority(string name)
    {
        if (name.Equals("CPU Package", StringComparison.OrdinalIgnoreCase))
        {
            return 0;
        }
        if (name.Equals("Package", StringComparison.OrdinalIgnoreCase))
        {
            return 1;
        }
        return name.Contains("Package", StringComparison.OrdinalIgnoreCase) ? 2 : int.MaxValue;
    }

    private static int GpuPriority(string name)
    {
        if (name.Contains("Total Board", StringComparison.OrdinalIgnoreCase))
        {
            return 0;
        }
        if (name.Contains("Board Power", StringComparison.OrdinalIgnoreCase))
        {
            return 1;
        }
        if (name.Equals("GPU Package", StringComparison.OrdinalIgnoreCase))
        {
            return 2;
        }
        if (name.Equals("GPU PPT", StringComparison.OrdinalIgnoreCase))
        {
            return 3;
        }
        if (name.Equals("GPU Power", StringComparison.OrdinalIgnoreCase))
        {
            return 4;
        }
        return name.Contains("Package", StringComparison.OrdinalIgnoreCase) ? 5 : int.MaxValue;
    }

    private static bool IsGpu(HardwareType type) => type is
        HardwareType.GpuAmd or HardwareType.GpuIntel or HardwareType.GpuNvidia;
}

internal static class PowerSnapshotCpuPreference
{
    public static PowerSnapshot Apply(PowerSnapshot hardwareSnapshot, CpuPowerReading nativeReading)
    {
        if (nativeReading.Status != MetricStatus.Ok
            || !double.IsFinite(nativeReading.Watts)
            || nativeReading.Watts <= 0
            || nativeReading.Watts > PowerSensorSelector.MaximumReasonableWatts)
        {
            return hardwareSnapshot;
        }
        return hardwareSnapshot with
        {
            CpuPackageStatus = MetricStatus.Ok,
            CpuPackageWatts = nativeReading.Watts,
        };
    }
}
