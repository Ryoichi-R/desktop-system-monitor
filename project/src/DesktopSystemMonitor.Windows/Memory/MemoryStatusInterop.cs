using System.Runtime.InteropServices;

namespace DesktopSystemMonitor.Windows.Memory;

internal static partial class MemoryStatusInterop
{
    [StructLayout(LayoutKind.Sequential)]
    internal struct MEMORYSTATUSEX
    {
        public uint Length;
        public uint MemoryLoad;
        public ulong TotalPhysical;
        public ulong AvailablePhysical;
        public ulong TotalPageFile;
        public ulong AvailablePageFile;
        public ulong TotalVirtual;
        public ulong AvailableVirtual;
        public ulong AvailableExtendedVirtual;
    }

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX buffer);

    internal static MemoryReading? Read()
    {
        var status = new MEMORYSTATUSEX
        {
            Length = (uint)Marshal.SizeOf<MEMORYSTATUSEX>(),
        };
        return GlobalMemoryStatusEx(ref status)
            ? new MemoryReading(status.TotalPhysical, status.AvailablePhysical)
            : null;
    }
}

internal readonly record struct MemoryReading(ulong TotalPhysicalBytes, ulong AvailablePhysicalBytes);
