using System.Runtime.InteropServices;

namespace DesktopSystemMonitor.Mac.SensorHost.Interop;

internal static unsafe partial class MachNative
{
    private const string Library = "/usr/lib/libSystem.B.dylib";
    [LibraryImport(Library, EntryPoint = "mach_host_self")]
    internal static partial uint HostSelf();
    [LibraryImport(Library, EntryPoint = "host_statistics")]
    internal static partial int HostStatistics(uint host, int flavor, uint* values, ref uint count);
    [LibraryImport(Library, EntryPoint = "host_statistics64")]
    internal static partial int HostStatistics64(uint host, int flavor, byte* values, ref uint count);
    [LibraryImport(Library, EntryPoint = "sysctlbyname", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial int Sysctl(string name, void* value, ref nuint length, IntPtr newValue, nuint newLength);
    [LibraryImport(Library, EntryPoint = "mach_port_deallocate")]
    internal static partial int ReleasePort(uint task, uint port);
}
