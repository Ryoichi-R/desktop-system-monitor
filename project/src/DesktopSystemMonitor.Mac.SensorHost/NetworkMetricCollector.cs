using System.Buffers.Binary;
using System.Diagnostics;
using System.Runtime.InteropServices;
using DesktopSystemMonitor.Core.Platform;
using DesktopSystemMonitor.Core.Sampling;
using DesktopSystemMonitor.Mac.SensorHost.Interop;

namespace DesktopSystemMonitor.Mac.SensorHost;

internal sealed record NetworkCounter(uint Index, string Name, uint Flags, ulong BaudRate, uint Received, uint Sent);

internal sealed class NetworkMetricCollector(Func<NetworkCounter[]> read)
{
    private sealed record Baseline(NetworkCounter Counter, RateCalculator Receive, RateCalculator Send);
    private readonly Dictionary<uint, Baseline> _baselines = new();
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
                // This tested Mac exposes byte counters rounded to 256 bytes and truncated to 32 bits.
                double maxBytesPerSecond = Math.Max(counter.BaudRate / 8d, 1_250_000_000d);
                bool reset = longGap || seconds * maxBytesPerSecond >= 4294967296d / 2
                    || !_baselines.TryGetValue(counter.Index, out var old) || old.Counter.Name != counter.Name
                    || old.Counter.Flags != counter.Flags || old.Counter.BaudRate != counter.BaudRate;
                if (!reset)
                {
                    old = _baselines[counter.Index];
                    reset = InvalidDelta(old.Counter.Received, counter.Received, seconds, maxBytesPerSecond)
                        || InvalidDelta(old.Counter.Sent, counter.Sent, seconds, maxBytesPerSecond);
                }
                if (reset) _baselines[counter.Index] = new(counter, new(1L << 32), new(1L << 32));
                var baseline = _baselines[counter.Index];
                var receive = baseline.Receive.Update(counter.Received, now, Stopwatch.Frequency);
                var send = baseline.Send.Update(counter.Sent, now, Stopwatch.Frequency);
                _baselines[counter.Index] = baseline with { Counter = counter };
                result.Add(new()
                {
                    InterfaceIndex = counter.Index,
                    Name = counter.Name,
                    ReceiveBytesPerSecond = receive.IsWarmingUp ? SensorHostMetricValue.WarmingUp() : SensorHostMetricValue.Ok(receive.RatePerSecond, now),
                    SendBytesPerSecond = send.IsWarmingUp ? SensorHostMetricValue.WarmingUp() : SensorHostMetricValue.Ok(send.RatePerSecond, now),
                });
            }
            return SensorHostMetricGroups.Ok(result.ToArray(), now);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or InvalidOperationException or OverflowException)
        {
            _baselines.Clear();
            return SensorHostMetricGroups.Unavailable<SensorHostNetworkInterfaceReading>();
        }
    }

    private static bool InvalidDelta(uint previous, uint current, double seconds, double limit)
    {
        if (current < previous && !(previous >= 0xC0000000 && current < 0x40000000)) return true;
        return unchecked(current - previous) > limit * seconds + 256;
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
                    unchecked((uint)BinaryPrimitives.ReadUInt64LittleEndian(buffer[96..])),
                    unchecked((uint)BinaryPrimitives.ReadUInt64LittleEndian(buffer[104..]))));
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
