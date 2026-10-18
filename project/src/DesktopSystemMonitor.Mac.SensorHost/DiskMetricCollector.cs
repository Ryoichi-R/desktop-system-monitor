using System.Diagnostics;
using DesktopSystemMonitor.Core.Platform;
using DesktopSystemMonitor.Core.Sampling;
using DesktopSystemMonitor.Mac.SensorHost.Interop;

namespace DesktopSystemMonitor.Mac.SensorHost;

internal readonly record struct DiskCounter(ulong Identity, long ReadBytes, long WrittenBytes);

internal sealed class DiskMetricCollector(Func<DiskCounter[]> read)
{
    private readonly RateCalculator _read = new(), _write = new();
    private DiskCounter[] _previous = [];
    private long _previousTick;
    private DateTimeOffset _previousWall;

    internal SensorHostMetrics Sample(SensorHostMetrics metrics, long now, DateTimeOffset wall)
    {
        try
        {
            var counters = read().OrderBy(x => x.Identity).ToArray();
            if (counters.Length is 0 or > 64 || counters.Any(x => x.Identity == 0 || x.ReadBytes < 0 || x.WrittenBytes < 0)
                || counters.Select(x => x.Identity).Distinct().Count() != counters.Length)
                throw new InvalidDataException("Invalid disk counters.");
            bool reset = _previous.Length != counters.Length || now <= _previousTick
                || (_previousTick != 0 && Stopwatch.GetElapsedTime(_previousTick, now).TotalSeconds > 3)
                || (wall - _previousWall).TotalSeconds is < 0 or > 3;
            for (int i = 0; !reset && i < counters.Length; i++)
                reset = counters[i].Identity != _previous[i].Identity
                    || counters[i].ReadBytes < _previous[i].ReadBytes || counters[i].WrittenBytes < _previous[i].WrittenBytes;
            if (reset) { _read.Reset(); _write.Reset(); }
            long readTotal = counters.Sum(x => x.ReadBytes), writeTotal = counters.Sum(x => x.WrittenBytes);
            var readRate = _read.Update(readTotal, now, Stopwatch.Frequency);
            var writeRate = _write.Update(writeTotal, now, Stopwatch.Frequency);
            _previous = counters;
            _previousTick = now;
            _previousWall = wall;
            return metrics with
            {
                DiskReadBytesPerSecond = Value(readRate, now),
                DiskWriteBytesPerSecond = Value(writeRate, now),
            };
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or InvalidOperationException
            or OverflowException or DllNotFoundException or EntryPointNotFoundException)
        {
            _previous = [];
            _previousTick = 0;
            _read.Reset(); _write.Reset();
            return metrics with { DiskReadBytesPerSecond = SensorHostMetricValue.Unavailable(), DiskWriteBytesPerSecond = SensorHostMetricValue.Unavailable() };
        }
    }

    private static SensorHostMetricValue Value(RateSample sample, long now) => sample.IsWarmingUp
        ? SensorHostMetricValue.WarmingUp() : SensorHostMetricValue.Ok(sample.RatePerSecond, now);
}

internal static class NativeDiskReader
{
    internal static DiskCounter[] Read()
    {
        nint matching = ExtendedNative.Matching("IOBlockStorageDriver");
        if (matching == 0) throw new IOException("Disk matching unavailable.");
        if (DiskNative.Services(0, matching, out uint iterator) != 0) throw new IOException("Disk services unavailable.");
        var counters = new List<DiskCounter>();
        try
        {
            uint service;
            while ((service = DiskNative.Next(iterator)) != 0)
            {
                try
                {
                    if (counters.Count >= 64) throw new InvalidDataException("Too many disk services.");
                    if (DiskNative.Identity(service, out ulong identity) != 0) throw new IOException("Disk identity unavailable.");
                    nint key = ExtendedNative.String(0, "Statistics", 0x08000100);
                    if (key == 0) throw new IOException("Disk key unavailable.");
                    nint dictionary;
                    try { dictionary = ExtendedNative.Property(service, key, 0, 0); }
                    finally { ExtendedNative.Release(key); }
                    if (dictionary == 0) throw new IOException("Disk statistics unavailable.");
                    try
                    {
                        if (ExtendedNative.TypeId(dictionary) != ExtendedNative.DictionaryType()) throw new InvalidDataException("Invalid disk dictionary.");
                        counters.Add(new(identity, Number(dictionary, "Bytes (Read)"), Number(dictionary, "Bytes (Write)")));
                    }
                    finally { ExtendedNative.Release(dictionary); }
                }
                finally { _ = ExtendedNative.ReleaseObject(service); }
            }
        }
        finally { if (iterator != 0) _ = ExtendedNative.ReleaseObject(iterator); }
        return counters.ToArray();
    }

    private static long Number(nint dictionary, string name)
    {
        nint key = ExtendedNative.String(0, name, 0x08000100);
        if (key == 0) throw new IOException("Disk counter key unavailable.");
        try
        {
            nint number = ExtendedNative.DictionaryValue(dictionary, key);
            if (number == 0 || ExtendedNative.TypeId(number) != ExtendedNative.NumberType()
                || !DiskNative.Number(number, 4, out long value) || value < 0)
                throw new InvalidDataException("Invalid disk counter.");
            return value;
        }
        finally { ExtendedNative.Release(key); }
    }
}
