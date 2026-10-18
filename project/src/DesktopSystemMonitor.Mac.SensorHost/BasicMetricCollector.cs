using System.Diagnostics;
using System.Runtime.InteropServices;
using DesktopSystemMonitor.Core.Platform;
using DesktopSystemMonitor.Mac.SensorHost.Interop;

namespace DesktopSystemMonitor.Mac.SensorHost;

internal interface IBasicMetricReader
{
    uint[] ReadCpuTicks();
    (ulong Used, ulong Total) ReadMemory();
}

internal sealed class BasicMetricCollector(IBasicMetricReader reader)
{
    private uint[]? _previous;
    private long _previousTime;

    internal SensorHostMetrics Sample()
    {
        var metrics = SensorHostMetrics.Empty;
        long now = Stopwatch.GetTimestamp();
        try
        {
            uint[] current = reader.ReadCpuTicks();
            double? percent = _previous is not null && Stopwatch.GetElapsedTime(_previousTime, now) <= TimeSpan.FromSeconds(10)
                ? CpuPercent(_previous, current) : null;
            _previous = current;
            _previousTime = now;
            metrics = metrics with { CpuUtilizationPercent = percent is double value ? SensorHostMetricValue.Ok(value, now) : SensorHostMetricValue.WarmingUp() };
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or OverflowException)
        {
            _previous = null;
        }
        try
        {
            var (used, total) = reader.ReadMemory();
            if (total == 0 || used > total || total > long.MaxValue) throw new InvalidOperationException("Invalid memory counters.");
            metrics = metrics with
            {
                MemoryUsedBytes = SensorHostMetricValue.Ok(used, now),
                MemoryTotalBytes = SensorHostMetricValue.Ok(total, now),
                MemoryUtilizationPercent = SensorHostMetricValue.Ok(used * 100.0 / total, now),
            };
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or OverflowException) { }
        return metrics;
    }

    internal static double? CpuPercent(uint[] previous, uint[] current)
    {
        if (previous.Length != 4 || current.Length != 4) return null;
        ulong total = 0, idle = 0;
        for (int i = 0; i < 4; i++)
        {
            uint delta = unchecked(current[i] - previous[i]);
            // Reject resets; only a small delta across uint wrap is accepted.
            if (delta > int.MaxValue) return null;
            total += delta;
            if (i == 2) idle = delta;
        }
        return total == 0 ? null : (total - idle) * 100.0 / total;
    }
}

internal sealed unsafe class MachBasicMetricReader : IBasicMetricReader
{
    public uint[] ReadCpuTicks()
    {
        if (!OperatingSystem.IsMacOS()) throw new InvalidOperationException("macOS is required.");
        uint host = MachNative.HostSelf();
        try
        {
            var values = new uint[4];
            uint count = 4;
            fixed (uint* pointer = values)
            {
                if (MachNative.HostStatistics(host, 3, pointer, ref count) != 0 || count != 4)
                    throw new IOException("CPU counters unavailable.");
            }
            return values;
        }
        finally { ReleaseHost(host); }
    }

    public (ulong Used, ulong Total) ReadMemory()
    {
        if (!OperatingSystem.IsMacOS()) throw new InvalidOperationException("macOS is required.");
        uint host = MachNative.HostSelf();
        try
        {
            byte* buffer = stackalloc byte[152];
            uint count = 38;
            if (MachNative.HostStatistics64(host, 4, buffer, ref count) != 0 || count < 36)
                throw new IOException("Memory counters unavailable.");
            // XNU vm_statistics64: internal - purgeable + wired + compressor pages.
            ulong internalPages = *(uint*)(buffer + 140), purgeable = *(uint*)(buffer + 88);
            if (purgeable > internalPages) throw new IOException("Inconsistent memory counters.");
            ulong pages = internalPages - purgeable + *(uint*)(buffer + 12) + *(uint*)(buffer + 128);
            return (checked(pages * ReadSysctl("hw.pagesize")), ReadSysctl("hw.memsize"));
        }
        finally { ReleaseHost(host); }
    }

    private static ulong ReadSysctl(string name)
    {
        ulong value = 0;
        nuint size = sizeof(ulong);
        if (MachNative.Sysctl(name, &value, ref size, IntPtr.Zero, 0) != 0 || (size != 4 && size != 8))
            throw new IOException("System counter unavailable.");
        return value;
    }

    private static void ReleaseHost(uint host)
    {
        IntPtr library = NativeLibrary.Load("/usr/lib/libSystem.B.dylib");
        try
        {
            uint task = unchecked((uint)Marshal.ReadInt32(NativeLibrary.GetExport(library, "mach_task_self_")));
            _ = MachNative.ReleasePort(task, host);
        }
        finally { NativeLibrary.Free(library); }
    }
}
