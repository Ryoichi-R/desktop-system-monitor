using System.Globalization;
using System.Text.RegularExpressions;

namespace DesktopSystemMonitor.Windows.Gpu;

/// <summary>
/// Parses PDH instance names of the form
/// <c>pid_1234_luid_0x00000000_0x0000ABCD_phys_0_eng_0_engtype_3D</c>.
///
/// The <c>_Total</c> pseudo-instance and any name that does not match are
/// treated as parse failures — the caller decides whether to include, skip, or
/// aggregate them separately.
/// </summary>
public static partial class GpuInstanceName
{
    [GeneratedRegex(
        @"^pid_(?<pid>\d+)_luid_0x(?<hi>[0-9A-Fa-f]+)_0x(?<lo>[0-9A-Fa-f]+)_phys_(?<phys>\d+)_eng_(?<eng>\d+)_engtype_(?<type>.+)$",
        RegexOptions.CultureInvariant)]
    private static partial Regex InstanceRegex();

    [GeneratedRegex(
        @"^luid_0x(?<hi>[0-9A-Fa-f]+)_0x(?<lo>[0-9A-Fa-f]+)_phys_(?<phys>\d+)$",
        RegexOptions.CultureInvariant)]
    private static partial Regex MemoryInstanceRegex();

    public static bool TryParse(string instance, out GpuEngineInstance parsed)
    {
        ArgumentNullException.ThrowIfNull(instance);
        parsed = default;
        var m = InstanceRegex().Match(instance);
        if (!m.Success)
        {
            return false;
        }
        if (!uint.TryParse(m.Groups["pid"].ValueSpan, NumberStyles.None, CultureInfo.InvariantCulture, out uint pid) ||
            !uint.TryParse(m.Groups["hi"].ValueSpan, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint hi) ||
            !uint.TryParse(m.Groups["lo"].ValueSpan, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint lo) ||
            !uint.TryParse(m.Groups["phys"].ValueSpan, NumberStyles.None, CultureInfo.InvariantCulture, out uint phys) ||
            !uint.TryParse(m.Groups["eng"].ValueSpan, NumberStyles.None, CultureInfo.InvariantCulture, out uint eng))
        {
            return false;
        }
        ulong luid = ((ulong)hi << 32) | lo;
        parsed = new GpuEngineInstance(pid, luid, phys, eng, m.Groups["type"].Value);
        return true;
    }

    public static bool TryParseMemory(string instance, out GpuMemoryInstance parsed)
    {
        ArgumentNullException.ThrowIfNull(instance);
        parsed = default;
        var m = MemoryInstanceRegex().Match(instance);
        if (!m.Success ||
            !uint.TryParse(m.Groups["hi"].ValueSpan, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint hi) ||
            !uint.TryParse(m.Groups["lo"].ValueSpan, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint lo) ||
            !uint.TryParse(m.Groups["phys"].ValueSpan, NumberStyles.None, CultureInfo.InvariantCulture, out uint phys))
        {
            return false;
        }
        parsed = new GpuMemoryInstance(((ulong)hi << 32) | lo, phys);
        return true;
    }
}

public readonly record struct GpuEngineInstance(uint Pid, ulong Luid, uint PhysicalAdapter, uint EngineIndex, string EngineType);
public readonly record struct GpuMemoryInstance(ulong Luid, uint PhysicalAdapter);
