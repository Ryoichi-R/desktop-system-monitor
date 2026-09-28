using System.Diagnostics;
using DesktopSystemMonitor.Core.Platform;
using DesktopSystemMonitor.Mac.SensorHost.Interop;

namespace DesktopSystemMonitor.Mac.SensorHost;

internal interface IGpuReader : IDisposable
{
    double Read();
    void Reset();
}

internal sealed class GpuMetricCollector(IGpuReader reader) : IDisposable
{
    private bool _primed;
    private long _previous;
    private DateTimeOffset _previousWall;
    internal SensorHostMetricValue Sample(long now, DateTimeOffset wall)
    {
        try
        {
            if (_primed && (Stopwatch.GetElapsedTime(_previous, now) > TimeSpan.FromSeconds(3)
                || (wall - _previousWall).TotalSeconds is < 0 or > 3))
            {
                reader.Reset();
                _primed = false;
            }
            double value = reader.Read();
            _previous = now;
            _previousWall = wall;
            if (!double.IsFinite(value) || value is < 0 or > 100) throw new InvalidDataException("Invalid GPU utilization.");
            if (!_primed) { _primed = true; return SensorHostMetricValue.WarmingUp(); }
            return SensorHostMetricValue.Ok(value, now);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or InvalidOperationException or DllNotFoundException or EntryPointNotFoundException)
        {
            _primed = false;
            reader.Reset();
            return SensorHostMetricValue.Unavailable();
        }
    }

    public void Dispose() => reader.Dispose();
}

internal sealed class NativeGpuReader : IGpuReader
{
    private uint _service;
    public double Read()
    {
        if (_service == 0)
        {
            _service = FindService("IOAccelerator");
            if (_service == 0) _service = FindService("AGXAccelerator");
            if (_service == 0) throw new IOException("GPU service unavailable.");
        }
        nint key = ExtendedNative.String(0, "PerformanceStatistics", 0x08000100);
        if (key == 0) throw new IOException("GPU property key unavailable.");
        nint dictionary;
        try { dictionary = ExtendedNative.Property(_service, key, 0, 0); }
        finally { ExtendedNative.Release(key); }
        if (dictionary == 0) throw new IOException("GPU statistics unavailable.");
        try
        {
            if (ExtendedNative.TypeId(dictionary) != ExtendedNative.DictionaryType()) throw new InvalidDataException("Invalid GPU dictionary.");
            nint valueKey = ExtendedNative.String(0, "Device Utilization %", 0x08000100);
            if (valueKey == 0) throw new IOException("GPU metric key unavailable.");
            try
            {
                nint number = ExtendedNative.DictionaryValue(dictionary, valueKey);
                if (number == 0 || ExtendedNative.TypeId(number) != ExtendedNative.NumberType()
                    || !ExtendedNative.NumberValue(number, 6, out double value)) throw new IOException("GPU metric unavailable.");
                return value;
            }
            finally { ExtendedNative.Release(valueKey); }
        }
        finally { ExtendedNative.Release(dictionary); }
    }

    private static uint FindService(string name)
    {
        nint matching = ExtendedNative.Matching(name);
        return matching == 0 ? 0 : ExtendedNative.Service(0, matching);
    }

    public void Reset()
    {
        if (_service == 0) return;
        _ = ExtendedNative.ReleaseObject(_service);
        _service = 0;
    }

    public void Dispose() => Reset();
}

internal sealed class NativeMetricCollector : IDisposable
{
    private readonly ProcessMetricCollector _processes = new(NativeProcessReader.Read);
    private readonly DiskMetricCollector _disk = new(NativeDiskReader.Read);
    private readonly ThermalMetricCollector _thermal = new(new SmcTemperatureReader(), new GpuEnergyReader());
    private readonly BasicMetricCollector _basic = new(new MachBasicMetricReader());
    private readonly NetworkMetricCollector _network = new(NativeNetworkReader.Read);
    private readonly GpuMetricCollector _gpu = new(new NativeGpuReader());

    internal SensorHostMetrics Sample()
    {
        var metrics = _basic.Sample();
        long now = Stopwatch.GetTimestamp();
        var wall = DateTimeOffset.UtcNow;
        return _thermal.Sample(_disk.Sample(metrics, now, wall) with { HighLoadProcesses = _processes.Sample(now, wall), NetworkInterfaces = _network.Sample(now, wall), GpuUtilizationPercent = _gpu.Sample(now, wall) }, now, wall);
    }

    public void Dispose() { _gpu.Dispose(); _thermal.Dispose(); }
}
