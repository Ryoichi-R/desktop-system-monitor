using System.Runtime.InteropServices;
using DesktopSystemMonitor.Core.Metrics;
using DesktopSystemMonitor.Windows.Disk;
using Xunit;

namespace DesktopSystemMonitor.Windows.Tests.Disk;

public sealed class VolumeDiskResolverTests
{
    private const int FirstExtentOffset = 8;
    private const int ExtentSize = 24;

    [Fact]
    public void returns_the_only_physical_disk_number()
    {
        IntPtr buffer = AllocateExtents(7);
        try
        {
            Assert.Equal(7, VolumeDiskResolver.ParseSingleDiskNumber(buffer, FirstExtentOffset + ExtentSize));
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    [Fact]
    public void accepts_multiple_extents_only_when_they_share_one_disk()
    {
        IntPtr sameDisk = AllocateExtents(3, 3);
        IntPtr differentDisks = AllocateExtents(3, 4);
        try
        {
            int returned = FirstExtentOffset + (2 * ExtentSize);
            Assert.Equal(3, VolumeDiskResolver.ParseSingleDiskNumber(sameDisk, returned));
            Assert.Null(VolumeDiskResolver.ParseSingleDiskNumber(differentDisks, returned));
            SystemDiskResolution resolution = VolumeDiskResolver.ParseDiskResolution(differentDisks, returned);
            Assert.Equal(DiskAvailabilityReason.SystemVolumeSpansMultipleDisks, resolution.Reason);
            Assert.False(resolution.Succeeded);
        }
        finally
        {
            Marshal.FreeHGlobal(sameDisk);
            Marshal.FreeHGlobal(differentDisks);
        }
    }

    [Fact]
    public void rejects_null_truncated_and_invalid_extent_counts()
    {
        IntPtr buffer = AllocateExtents(1);
        try
        {
            Assert.Null(VolumeDiskResolver.ParseSingleDiskNumber(IntPtr.Zero, 0));
            Assert.Equal(
                DiskAvailabilityReason.SystemDiskResolveFailed,
                VolumeDiskResolver.ParseDiskResolution(IntPtr.Zero, 0).Reason);
            Assert.Null(VolumeDiskResolver.ParseSingleDiskNumber(buffer, FirstExtentOffset));
            Marshal.WriteInt32(buffer, 0, 0);
            Assert.Null(VolumeDiskResolver.ParseSingleDiskNumber(buffer, FirstExtentOffset + ExtentSize));
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static IntPtr AllocateExtents(params int[] disks)
    {
        int bytes = FirstExtentOffset + (disks.Length * ExtentSize);
        IntPtr buffer = Marshal.AllocHGlobal(bytes);
        for (int offset = 0; offset < bytes; offset++)
        {
            Marshal.WriteByte(buffer, offset, 0);
        }
        Marshal.WriteInt32(buffer, 0, disks.Length);
        for (int index = 0; index < disks.Length; index++)
        {
            Marshal.WriteInt32(buffer, FirstExtentOffset + (index * ExtentSize), disks[index]);
        }
        return buffer;
    }
}
