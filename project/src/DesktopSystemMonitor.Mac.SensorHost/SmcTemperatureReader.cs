using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Text;
using DesktopSystemMonitor.Mac.SensorHost.Interop;

namespace DesktopSystemMonitor.Mac.SensorHost;

internal interface ITemperatureReader : IDisposable
{
    bool Supported { get; }
    double? Read(string key);
}

internal sealed unsafe class SmcTemperatureReader : ITemperatureReader
{
    private uint _connection;
    private bool? _supported;
    public bool Supported => _supported ??= SystemString("hw.model") == TemperatureProfile.Model
        && SystemString("kern.osproductversion") == TemperatureProfile.OsVersion;

    private static string SystemString(string name)
    {
        byte* buffer = stackalloc byte[128];
        nuint length = 128;
        if (MachNative.Sysctl(name, buffer, ref length, 0, 0) != 0 || length is 0 or > 128) throw new IOException("Platform identity unavailable.");
        return Encoding.UTF8.GetString(new ReadOnlySpan<byte>(buffer, (int)length)).TrimEnd('\0');
    }

    public double? Read(string key)
    {
        if (!Supported) return null;
        if (_connection == 0)
        {
            nint matching = ExtendedNative.Matching("AppleSMC");
            uint service = matching == 0 ? 0 : ExtendedNative.Service(0, matching);
            if (service == 0) throw new IOException("Temperature service unavailable.");
            nint library = NativeLibrary.Load("/usr/lib/libSystem.B.dylib");
            try
            {
                uint task = unchecked((uint)Marshal.ReadInt32(NativeLibrary.GetExport(library, "mach_task_self_")));
                if (ThermalNative.Open(service, task, 0, out _connection) != 0) throw new IOException("Temperature connection unavailable.");
            }
            finally { NativeLibrary.Free(library); _ = ExtendedNative.ReleaseObject(service); }
        }
        byte* input = stackalloc byte[80];
        byte* output = stackalloc byte[80];
        new Span<byte>(input, 80).Clear();
        uint code = FourCc(key);
        BinaryPrimitives.WriteUInt32LittleEndian(new Span<byte>(input, 4), code);
        input[42] = 9;
        if (!Call(input, output)) return null;
        uint size = BinaryPrimitives.ReadUInt32LittleEndian(new ReadOnlySpan<byte>(output + 28, 4));
        uint type = BinaryPrimitives.ReadUInt32LittleEndian(new ReadOnlySpan<byte>(output + 32, 4));
        if (size is 0 or > 32) return null;
        new Span<byte>(input, 80).Clear();
        BinaryPrimitives.WriteUInt32LittleEndian(new Span<byte>(input, 4), code);
        BinaryPrimitives.WriteUInt32LittleEndian(new Span<byte>(input + 28, 4), size);
        input[42] = 5;
        return Call(input, output) ? Decode(type, new ReadOnlySpan<byte>(output + 48, (int)size)) : null;
    }

    private bool Call(byte* input, byte* output)
    {
        new Span<byte>(output, 80).Clear();
        nuint size = 80;
        return ThermalNative.Call(_connection, 2, input, 80, output, ref size) == 0 && size == 80 && output[40] == 0;
    }

    internal static uint FourCc(string key)
    {
        if (key.Length != 4) throw new ArgumentException("Four characters required.", nameof(key));
        return (uint)(key[0] << 24 | key[1] << 16 | key[2] << 8 | key[3]);
    }

    internal static double? Decode(uint type, ReadOnlySpan<byte> bytes)
    {
        if (type == FourCc("flt ") && bytes.Length == 4) return BinaryPrimitives.ReadSingleLittleEndian(bytes);
        if (type == FourCc("sp78") && bytes.Length == 2) return BinaryPrimitives.ReadInt16BigEndian(bytes) / 256d;
        if (type == FourCc("ioft") && bytes.Length == 8) return BinaryPrimitives.ReadInt64LittleEndian(bytes) / 65536d;
        return null;
    }

    public void Dispose()
    {
        if (_connection != 0) _ = ThermalNative.Close(_connection);
        _connection = 0;
    }
}
