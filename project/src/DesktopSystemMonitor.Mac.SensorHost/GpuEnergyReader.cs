using System.Text;
using DesktopSystemMonitor.Mac.SensorHost.Interop;

namespace DesktopSystemMonitor.Mac.SensorHost;

internal interface IGpuEnergyReader : IDisposable
{
    double? ReadJoules();
    void Reset();
}

internal sealed unsafe class GpuEnergyReader : IGpuEnergyReader
{
    private nint _channels, _subscription, _subscribed, _previous;

    public double? ReadJoules()
    {
        if (_subscription == 0) Initialize();
        nint current = ThermalNative.Samples(_subscription, _channels, 0);
        if (current == 0) throw new IOException("Energy sample unavailable.");
        if (_previous == 0) { _previous = current; return null; }
        nint delta = 0;
        try
        {
            delta = ThermalNative.Delta(_previous, current, 0);
            nint array = ChannelArray(delta);
            if (ThermalNative.Count(array) != 1) throw new InvalidDataException("Unexpected energy channel count.");
            nint channel = ThermalNative.At(array, 0);
            RequireDictionary(channel);
            if (Text(ThermalNative.ChannelName(channel)) != "GPU Energy") throw new InvalidDataException("Unexpected energy channel.");
            return ToJoules(ThermalNative.Integer(channel, 0), Text(ThermalNative.Unit(channel)));
        }
        finally
        {
            if (delta != 0) ExtendedNative.Release(delta);
            ExtendedNative.Release(_previous);
            _previous = current;
        }
    }

    private void Initialize()
    {
        nint group = ExtendedNative.String(0, "Energy Model", 0x08000100);
        if (group == 0) throw new IOException("Energy group unavailable.");
        nint channels;
        try { channels = ThermalNative.Channels(group, 0, 0, 0, 0); }
        finally { ExtendedNative.Release(group); }
        if (channels == 0) throw new IOException("Energy channels unavailable.");
        try { _channels = ThermalNative.Copy(0, channels, 1); }
        finally { ExtendedNative.Release(channels); }
        nint array = ChannelArray(_channels);
        nint count = ThermalNative.Count(array);
        if (count is < 1 or > 4096) throw new InvalidDataException("Invalid energy channel count.");
        for (nint i = count - 1; i >= 0; i--)
        {
            nint channel = ThermalNative.At(array, i);
            RequireDictionary(channel);
            if (Text(ThermalNative.ChannelName(channel)) != "GPU Energy") ThermalNative.Remove(array, i);
        }
        if (ThermalNative.Count(array) != 1) throw new InvalidDataException("GPU energy channel missing or ambiguous.");
        _subscription = ThermalNative.Subscribe(0, _channels, out _subscribed, 0, 0);
        if (_subscription == 0 || _subscribed == 0) throw new IOException("Energy subscription unavailable.");
    }

    private static nint ChannelArray(nint dictionary)
    {
        RequireDictionary(dictionary);
        nint key = ExtendedNative.String(0, "IOReportChannels", 0x08000100);
        if (key == 0) throw new IOException("Energy channel key unavailable.");
        nint value;
        try { value = ExtendedNative.DictionaryValue(dictionary, key); }
        finally { ExtendedNative.Release(key); }
        if (value == 0 || ExtendedNative.TypeId(value) != ThermalNative.ArrayType()) throw new InvalidDataException("Invalid energy array.");
        return value;
    }

    private static void RequireDictionary(nint value)
    {
        if (value == 0 || ExtendedNative.TypeId(value) != ExtendedNative.DictionaryType()) throw new InvalidDataException("Invalid energy dictionary.");
    }

    private static string Text(nint value)
    {
        if (value == 0 || ExtendedNative.TypeId(value) != ThermalNative.StringType()) throw new InvalidDataException("Invalid energy string.");
        byte* buffer = stackalloc byte[128];
        new Span<byte>(buffer, 128).Clear();
        if (!ThermalNative.GetString(value, buffer, 128, 0x08000100)) throw new InvalidDataException("Energy string too long.");
        var bytes = new ReadOnlySpan<byte>(buffer, 128);
        int length = bytes.IndexOf((byte)0);
        if (length < 0) throw new InvalidDataException("Unterminated energy string.");
        return Encoding.UTF8.GetString(bytes[..length]);
    }

    internal static double ToJoules(long value, string unit)
    {
        if (value < 0) throw new InvalidDataException("Negative energy.");
        return unit switch
        {
            "nJ" => value / 1e9,
            "uJ" => value / 1e6,
            "mJ" => value / 1e3,
            "J" => value,
            _ => throw new InvalidDataException("Unknown energy unit."),
        };
    }

    public void Reset()
    {
        foreach (nint value in new[] { _previous, _subscription, _subscribed, _channels })
            if (value != 0) ExtendedNative.Release(value);
        _previous = _subscription = _subscribed = _channels = 0;
    }

    public void Dispose() => Reset();
}
