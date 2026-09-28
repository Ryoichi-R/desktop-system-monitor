using System.Buffers.Binary;
using System.Diagnostics;
using System.Text;
using DesktopSystemMonitor.Core.Platform;
using DesktopSystemMonitor.Mac.SensorHost.Interop;

namespace DesktopSystemMonitor.Mac.SensorHost;

internal sealed record ProcessCounter(int Pid, ulong Start, string Name, ulong CpuTicks, long ReadBytes, long WrittenBytes);
internal sealed record ProcessCounters(ProcessCounter[] Values, double SecondsPerCpuTick, int ProcessorCount);

internal sealed class ProcessMetricCollector(Func<ProcessCounters> read)
{
    private Dictionary<int, ProcessCounter> _previous = new();
    private long _tick;
    private DateTimeOffset _wall;

    internal SensorHostMetricGroup<SensorHostProcessReading> Sample(long now, DateTimeOffset wall)
    {
        try
        {
            var sample = read();
            if (sample.Values.Length > 8192 || sample.ProcessorCount <= 0 || !double.IsFinite(sample.SecondsPerCpuTick) || sample.SecondsPerCpuTick <= 0)
                throw new InvalidDataException("Invalid process sample.");
            var current = new Dictionary<int, ProcessCounter>();
            foreach (var value in sample.Values)
            {
                if (value.Pid <= 0 || value.Start == 0 || value.ReadBytes < 0 || value.WrittenBytes < 0
                    || string.IsNullOrWhiteSpace(value.Name) || value.Name.Length > 256 || value.Name.Any(char.IsControl) || !current.TryAdd(value.Pid, value))
                    throw new InvalidDataException("Invalid process counter.");
            }
            double seconds = (now - _tick) / (double)Stopwatch.Frequency;
            bool warmup = _tick == 0 || seconds is <= 0 or > 3 || (wall - _wall).TotalSeconds is < 0 or > 3;
            var result = new List<SensorHostProcessReading>();
            if (!warmup)
            {
                foreach (var value in current.Values)
                {
                    if (!_previous.TryGetValue(value.Pid, out var previous) || value.Start != previous.Start || value.Name != previous.Name
                        || value.CpuTicks < previous.CpuTicks || value.ReadBytes < previous.ReadBytes || value.WrittenBytes < previous.WrittenBytes) continue;
                    double cpu = (value.CpuTicks - previous.CpuTicks) * sample.SecondsPerCpuTick / seconds / sample.ProcessorCount * 100;
                    if (!double.IsFinite(cpu) || cpu is < 0 or > 100) continue;
                    result.Add(new()
                    {
                        ProcessId = value.Pid, Name = value.Name,
                        CpuUtilizationPercent = SensorHostMetricValue.Ok(cpu, now),
                        // Physical footprint is not Windows private bytes; do not reuse that field.
                        PrivateBytes = SensorHostMetricValue.Unavailable(),
                        IoReadBytesPerSecond = SensorHostMetricValue.Ok((value.ReadBytes - previous.ReadBytes) / seconds, now),
                        IoWriteBytesPerSecond = SensorHostMetricValue.Ok((value.WrittenBytes - previous.WrittenBytes) / seconds, now),
                    });
                }
            }
            _previous = current; _tick = now; _wall = wall;
            return warmup
                ? new() { Status = SensorHostMetricStatus.WarmingUp, Values = [], SampledAtMonotonicTicks = 0 }
                : SensorHostMetricGroups.Ok(result.OrderByDescending(x => x.CpuUtilizationPercent.Value).ThenBy(x => x.ProcessId).Take(10).ToArray(), now);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or InvalidOperationException or OverflowException or DllNotFoundException or EntryPointNotFoundException)
        {
            _previous.Clear(); _tick = 0;
            return SensorHostMetricGroups.Unavailable<SensorHostProcessReading>();
        }
    }
}

internal static unsafe class NativeProcessReader
{
    internal static ProcessCounters Read()
    {
        if (ProcessNative.Timebase(out var timebase) != 0 || timebase.Numerator == 0 || timebase.Denominator == 0)
            throw new IOException("Process clock unavailable.");
        int size = ProcessNative.List(1, 0, null, 0);
        if (size <= 0 || size > (8192 - 64) * sizeof(int)) throw new InvalidDataException("Process list size invalid.");
        int[] pids = new int[(size + 3) / sizeof(int) + 64];
        fixed (int* pointer = pids) size = ProcessNative.List(1, 0, pointer, pids.Length * sizeof(int));
        if (size <= 0 || size >= pids.Length * sizeof(int) || size % sizeof(int) != 0) throw new InvalidDataException("Process list truncated.");
        byte* usage = stackalloc byte[160];
        byte* name = stackalloc byte[256];
        var result = new List<ProcessCounter>();
        foreach (int pid in pids.AsSpan(0, size / sizeof(int)))
        {
            if (pid <= 0) continue;
            new Span<byte>(usage, 160).Clear();
            if (ProcessNative.Usage(pid, 2, usage) != 0) continue; // Process exited or permission denied.
            new Span<byte>(name, 256).Clear();
            int nameLength = ProcessNative.Name(pid, name, 256);
            if (nameLength <= 0 || nameLength >= 256) continue;
            string processName = Encoding.UTF8.GetString(new ReadOnlySpan<byte>(name, nameLength)).TrimEnd('\0');
            // Re-read the identity after obtaining the name to reject PID reuse.
            var first = Parse(pid, processName, new ReadOnlySpan<byte>(usage, 160));
            if (first is null || ProcessNative.Usage(pid, 2, usage) != 0) continue;
            if (first.Start != BinaryPrimitives.ReadUInt64LittleEndian(new ReadOnlySpan<byte>(usage + 80, 8))) continue;
            result.Add(first);
        }
        return new(result.ToArray(), timebase.Numerator / (double)timebase.Denominator / 1e9, Environment.ProcessorCount);
    }

    internal static ProcessCounter? Parse(int pid, string name, ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length != 160) throw new InvalidDataException("Process usage size invalid.");
        ulong user = BinaryPrimitives.ReadUInt64LittleEndian(bytes[16..]);
        ulong system = BinaryPrimitives.ReadUInt64LittleEndian(bytes[24..]);
        ulong start = BinaryPrimitives.ReadUInt64LittleEndian(bytes[80..]);
        ulong read = BinaryPrimitives.ReadUInt64LittleEndian(bytes[144..]);
        ulong write = BinaryPrimitives.ReadUInt64LittleEndian(bytes[152..]);
        if (pid <= 0 || start == 0 || ulong.MaxValue - user < system || read > long.MaxValue || write > long.MaxValue
            || string.IsNullOrWhiteSpace(name) || name.Length > 256 || name.Any(char.IsControl)) return null;
        return new(pid, start, name, user + system, (long)read, (long)write);
    }
}
