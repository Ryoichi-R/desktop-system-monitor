using System.Runtime.InteropServices;
using DesktopSystemMonitor.Core.Metrics;
using Microsoft.Win32.SafeHandles;

namespace DesktopSystemMonitor.Windows.Disk;

internal interface IVolumeDiskResolver
{
    SystemDiskResolution ResolveSystemDisk();
}

internal readonly record struct SystemDiskResolution(int? DiskNumber, DiskAvailabilityReason Reason)
{
    public bool Succeeded => DiskNumber is not null && Reason == DiskAvailabilityReason.None;

    public static SystemDiskResolution Success(int diskNumber) => new(diskNumber, DiskAvailabilityReason.None);

    public static SystemDiskResolution Failed(DiskAvailabilityReason reason) => new(null, reason);
}

internal sealed class VolumeDiskResolver : IVolumeDiskResolver
{
    private const uint IoctlVolumeGetVolumeDiskExtents = 0x00560000;
    private const uint QueryOnlyAccess = 0;
    private const uint ShareRead = 0x1;
    private const uint ShareWrite = 0x2;
    private const uint OpenExisting = 3;
    private const int ErrorMoreData = 234;

    public SystemDiskResolution ResolveSystemDisk()
    {
        string? root = Path.GetPathRoot(Environment.SystemDirectory);
        if (string.IsNullOrWhiteSpace(root) || root.Length < 2)
        {
            return SystemDiskResolution.Failed(DiskAvailabilityReason.SystemDiskResolveFailed);
        }
        string volumePath = $@"\\.\{root[..2]}";
        using SafeFileHandle handle = VolumeDiskInterop.CreateFile(volumePath, QueryOnlyAccess, ShareRead | ShareWrite, IntPtr.Zero, OpenExisting, 0, IntPtr.Zero);
        if (handle.IsInvalid)
        {
            return SystemDiskResolution.Failed(DiskAvailabilityReason.SystemDiskResolveFailed);
        }

        int bufferSize = 256;
        for (int attempt = 0; attempt < 2; attempt++)
        {
            IntPtr buffer = Marshal.AllocHGlobal(bufferSize);
            try
            {
                bool success = VolumeDiskInterop.DeviceIoControl(handle, IoctlVolumeGetVolumeDiskExtents, IntPtr.Zero, 0, buffer, bufferSize, out int returned, IntPtr.Zero);
                int error = success ? 0 : Marshal.GetLastWin32Error();
                if (!success && error == ErrorMoreData)
                {
                    bufferSize = Math.Max(bufferSize * 4, returned + 128);
                    continue;
                }
                if (!success)
                {
                    return SystemDiskResolution.Failed(DiskAvailabilityReason.SystemDiskResolveFailed);
                }
                return ParseDiskResolution(buffer, returned);
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }
        return SystemDiskResolution.Failed(DiskAvailabilityReason.SystemDiskResolveFailed);
    }

    internal static SystemDiskResolution ParseDiskResolution(IntPtr buffer, int returned)
    {
        if (buffer == IntPtr.Zero || returned < 8)
        {
            return SystemDiskResolution.Failed(DiskAvailabilityReason.SystemDiskResolveFailed);
        }
        int count = Marshal.ReadInt32(buffer);
        int firstOffset = Marshal.OffsetOf<VolumeDiskExtentsHeader>(nameof(VolumeDiskExtentsHeader.FirstExtent)).ToInt32();
        int extentSize = Marshal.SizeOf<DiskExtent>();
        long requiredBytes = firstOffset + ((long)count * extentSize);
        if (count <= 0 || requiredBytes > returned)
        {
            return SystemDiskResolution.Failed(DiskAvailabilityReason.SystemDiskResolveFailed);
        }
        var disks = new HashSet<int>();
        for (int index = 0; index < count; index++)
        {
            IntPtr current = IntPtr.Add(buffer, firstOffset + (index * extentSize));
            disks.Add(Marshal.PtrToStructure<DiskExtent>(current).DiskNumber);
        }
        return disks.Count == 1
            ? SystemDiskResolution.Success(disks.Single())
            : SystemDiskResolution.Failed(DiskAvailabilityReason.SystemVolumeSpansMultipleDisks);
    }

    internal static int? ParseSingleDiskNumber(IntPtr buffer, int returned) =>
        ParseDiskResolution(buffer, returned).DiskNumber;

    [StructLayout(LayoutKind.Sequential)]
    private struct VolumeDiskExtentsHeader
    {
        public int NumberOfDiskExtents;
        public DiskExtent FirstExtent;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DiskExtent
    {
        public int DiskNumber;
        public long StartingOffset;
        public long ExtentLength;
    }

}
