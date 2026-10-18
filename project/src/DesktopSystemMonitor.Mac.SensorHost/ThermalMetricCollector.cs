using System.Diagnostics;
using DesktopSystemMonitor.Core.Platform;

namespace DesktopSystemMonitor.Mac.SensorHost;

internal sealed class ThermalMetricCollector(ITemperatureReader temperature, IGpuEnergyReader energy) : IDisposable
{
    private long _previous;
    private DateTimeOffset? _wall;
    private double? _lastWatts;

    internal SensorHostMetrics Sample(SensorHostMetrics metrics, long now, DateTimeOffset wall)
    {
        SensorHostMetricValue cpu = SensorHostMetricValue.Unavailable(), gpu = SensorHostMetricValue.Unavailable();
        try
        {
            if (temperature.Supported)
            {
                cpu = ReadTemperature(TemperatureProfile.Cpu, now);
                gpu = ReadTemperature(TemperatureProfile.Gpu, now);
            }
        }
        catch (Exception ex) when (IsRecoverable(ex)) { }
        var power = SensorHostMetricValue.Unavailable();
        try
        {
            double seconds = (now - _previous) / (double)Stopwatch.Frequency;
            if (_wall is { } last && (seconds is <= 0 or > 3 || (wall - last).TotalSeconds is < 0 or > 3))
            { energy.Reset(); _lastWatts = null; }
            double? joules = energy.ReadJoules();
            if (joules is null) { power = SensorHostMetricValue.WarmingUp(); _lastWatts = null; }
            else
            {
                double watts = joules.Value / seconds;
                if (!double.IsFinite(watts) || watts is < 0 or > 1000) throw new InvalidDataException("Invalid GPU power.");
                power = _lastWatts is double previous ? SensorHostMetricValue.Ok((previous + watts) / 2, now) : SensorHostMetricValue.WarmingUp();
                _lastWatts = watts;
            }
        }
        catch (Exception ex) when (IsRecoverable(ex)) { energy.Reset(); _lastWatts = null; }
        _previous = now; _wall = wall;
        return metrics with { CpuTemperatureCelsius = cpu, GpuTemperatureCelsius = gpu, GpuPowerWatts = power };
    }

    private SensorHostMetricValue ReadTemperature(string[] keys, long now)
    {
        int valid = 0;
        double maximum = double.NegativeInfinity;
        foreach (string key in keys)
        {
            try
            {
                double? value = temperature.Read(key);
                if (value is >= 5 and <= 120 && double.IsFinite(value.Value)) { valid++; maximum = Math.Max(maximum, value.Value); }
            }
            catch (Exception ex) when (IsRecoverable(ex)) { }
        }
        // More than 10% missing/invalid requires recalibration, not a potentially misleading maximum.
        return valid * 10 >= keys.Length * 9 ? SensorHostMetricValue.Ok(maximum, now) : SensorHostMetricValue.Unavailable();
    }

    private static bool IsRecoverable(Exception ex) => ex is IOException or InvalidDataException or InvalidOperationException
        or DllNotFoundException or EntryPointNotFoundException or OverflowException;

    public void Dispose() { temperature.Dispose(); energy.Dispose(); }
}
