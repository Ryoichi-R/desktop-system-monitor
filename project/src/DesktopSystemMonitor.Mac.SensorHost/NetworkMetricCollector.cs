using System.Buffers.Binary;
using System.Diagnostics;
using DesktopSystemMonitor.Core.Platform;
using DesktopSystemMonitor.Mac.SensorHost.Interop;

namespace DesktopSystemMonitor.Mac.SensorHost;

internal sealed record NetworkCounter(uint Index, string Name, uint Flags, ulong BaudRate, ulong Received, ulong Sent, bool Is32Bit = false);

internal sealed class NetworkMetricCollector(Func<NetworkCounter[]> read)
{
    private readonly Dictionary<uint, NetworkCounter> _baselines = new();
    private long _previousTime;
    private DateTimeOffset _previousWall;

    internal SensorHostMetricGroup<SensorHostNetworkInterfaceReading> Sample(long now, DateTimeOffset wall)
    {
        try
        {
            var counters = read();
            if (counters.Length > SensorHostProtocol.MaxNetworkInterfaces || counters.Select(c => c.Index).Distinct().Count() != counters.Length)
                throw new InvalidDataException("Invalid interface collection.");
            double seconds = (now - _previousTime) / (double)Stopwatch.Frequency;
            bool longGap = seconds <= 0 || seconds > 3 || (wall - _previousWall).TotalSeconds is < 0 or > 3;
            _previousTime = now;
            _previousWall = wall;
            var active = counters.Where(c => (c.Flags & 1) != 0 && (c.Flags & 8) == 0
                && c.Name.StartsWith("en", StringComparison.Ordinal)).ToArray();
            foreach (uint index in _baselines.Keys.Except(active.Select(c => c.Index)).ToArray()) _baselines.Remove(index);
            var result = new List<SensorHostNetworkInterfaceReading>();
            foreach (var counter in active)
            {
                double maxBytesPerSecond = Math.Max(counter.BaudRate / 8d, 1_250_000_000d);
                bool ambiguous = counter.Is32Bit && (counter.BaudRate == 0 || seconds * maxBytesPerSecond >= (1UL << 31));
                var receive = SensorHostMetricValue.WarmingUp();
                var send = SensorHostMetricValue.WarmingUp();
                if (ambiguous && !longGap)
                {
                    receive = send = SensorHostMetricValue.Unavailable(SensorHostMetricReason.CounterRange);
                }
                else if (!longGap && _baselines.TryGetValue(counter.Index, out var previous)
                    && previous.Name == counter.Name && previous.Flags == counter.Flags
                    && previous.BaudRate == counter.BaudRate && previous.Is32Bit == counter.Is32Bit
                    && TryDelta(previous.Received, counter.Received, counter.Is32Bit, seconds * maxBytesPerSecond, out ulong received)
                    && TryDelta(previous.Sent, counter.Sent, counter.Is32Bit, seconds * maxBytesPerSecond, out ulong sent))
                {
                    receive = SensorHostMetricValue.Ok(received / seconds, now);
                    send = SensorHostMetricValue.Ok(sent / seconds, now);
                }
                _baselines[counter.Index] = counter;
                result.Add(new()
                {
                    InterfaceIndex = counter.Index,
                    Name = counter.Name,
                    ReceiveBytesPerSecond = receive,
                    SendBytesPerSecond = send,
                });
            }
            return SensorHostMetricGroups.Ok(result.ToArray(), now);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or InvalidOperationException or OverflowException)
        {
            _baselines.Clear();
            _previousTime = 0;
            _previousWall = default;
            return SensorHostMetricGroups.Unavailable<SensorHostNetworkInterfaceReading>();
        }
    }

    private static bool TryDelta(ulong previous, ulong current, bool is32Bit, double limit, out ulong delta)
    {
        delta = 0;
        if (is32Bit && (previous > uint.MaxValue || current > uint.MaxValue)) return false;
        if (current >= previous) delta = current - previous;
        else if (is32Bit && previous >= 0xC0000000 && current < 0x40000000) delta = (1UL << 32) - previous + current;
        else return false;
        return delta <= long.MaxValue && delta <= limit + 256;
    }
}

internal static unsafe class NativeNetworkReader
{
    internal static NetworkCounter[] Read()
    {
        int* mib = stackalloc int[] { 4, 17, 0, 0, 6, 0 };
        nuint length = 0;
        if (ExtendedNative.Sysctl(mib, 6, null, ref length, 0, 0) != 0 || length is 0 or > 1048576)
            throw new IOException("Network buffer unavailable.");
        byte[] buffer = new byte[(int)length];
        fixed (byte* pointer = buffer)
            if (ExtendedNative.Sysctl(mib, 6, pointer, ref length, 0, 0) != 0 || length > (nuint)buffer.Length)
                throw new IOException("Network buffer changed.");
        return Parse(buffer.AsSpan(0, (int)length), Name);
    }

    internal static NetworkCounter[] Parse(ReadOnlySpan<byte> buffer, Func<uint, string> name)
    {
        var result = new List<NetworkCounter>();
        while (!buffer.IsEmpty)
        {
            if (buffer.Length < 4) throw new InvalidDataException("Truncated routing header.");
            int length = BinaryPrimitives.ReadUInt16LittleEndian(buffer);
            if (length < 4 || length > buffer.Length || buffer[2] != 5) throw new InvalidDataException("Invalid routing message.");
            if (buffer[3] == 0x12)
            {
                if (length < 160) throw new InvalidDataException("Truncated interface statistics.");
                uint index = BinaryPrimitives.ReadUInt16LittleEndian(buffer[12..]);
                if (index == 0 || result.Count >= SensorHostProtocol.MaxNetworkInterfaces) throw new InvalidDataException("Invalid interface index/count.");
                result.Add(new(index, name(index), BinaryPrimitives.ReadUInt32LittleEndian(buffer[8..]),
                    BinaryPrimitives.ReadUInt64LittleEndian(buffer[48..]),
                    BinaryPrimitives.ReadUInt64LittleEndian(buffer[96..]),
                    BinaryPrimitives.ReadUInt64LittleEndian(buffer[104..])));
            }
            buffer = buffer[length..];
        }
        return result.ToArray();
    }

    private static string Name(uint index)
    {
        byte* value = stackalloc byte[16];
        new Span<byte>(value, 16).Clear();
        if (ExtendedNative.InterfaceName(index, value) == 0) throw new IOException("Interface disappeared.");
        int length = new ReadOnlySpan<byte>(value, 16).IndexOf((byte)0);
        if (length <= 0) throw new InvalidDataException("Invalid interface name.");
        return System.Text.Encoding.ASCII.GetString(new ReadOnlySpan<byte>(value, length));
    }
}
