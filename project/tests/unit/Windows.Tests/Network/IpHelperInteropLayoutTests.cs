using System.Runtime.InteropServices;
using DesktopSystemMonitor.Windows.Network;
using Xunit;

namespace DesktopSystemMonitor.Windows.Tests.Network;

/// <summary>
/// Pins the managed <see cref="IpHelperInterop.MIB_IF_ROW2"/> layout to the native
/// x64/ARM64 layout. A drifting offset would silently make the flag and counter
/// reads in <c>NetworkMetricSource</c> read the wrong bytes.
/// </summary>
[Trait("Category", "WindowsUnit")]
public class IpHelperInteropLayoutTests
{
    // The 32-bit layout aligns 8-byte fields differently, so the native offsets below
    // only apply to 64-bit processes.
    private static bool Is64Bit => Environment.Is64BitProcess;

    [Fact]
    public void mib_if_row2_has_the_native_64bit_size()
    {
        if (!Is64Bit)
        {
            return;
        }

        Assert.Equal(1352, Marshal.SizeOf<IpHelperInterop.MIB_IF_ROW2>());
    }

    [Theory]
    [InlineData(nameof(IpHelperInterop.MIB_IF_ROW2.InterfaceAndOperStatusFlags), 1152)]
    [InlineData(nameof(IpHelperInterop.MIB_IF_ROW2.OperStatus), 1156)]
    [InlineData(nameof(IpHelperInterop.MIB_IF_ROW2.InOctets), 1208)]
    [InlineData(nameof(IpHelperInterop.MIB_IF_ROW2.OutOctets), 1280)]
    public void mib_if_row2_fields_sit_at_the_native_64bit_offsets(string field, int expectedOffset)
    {
        if (!Is64Bit)
        {
            return;
        }

        Assert.Equal(expectedOffset, (int)Marshal.OffsetOf<IpHelperInterop.MIB_IF_ROW2>(field));
    }

    [Fact]
    public void interface_flag_constants_match_the_documented_bits()
    {
        Assert.Equal(0x01, IpHelperInterop.IF_FLAG_HARDWARE_INTERFACE);
        Assert.Equal(0x02, IpHelperInterop.IF_FLAG_FILTER_INTERFACE);
    }
}
