using System.Runtime.InteropServices;

namespace DesktopSystemMonitor.Mac.SensorHost.Interop;

internal static partial class DiskNative
{
    private const string Io = "/System/Library/Frameworks/IOKit.framework/IOKit";
    private const string Cf = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";
    [LibraryImport(Io, EntryPoint = "IOServiceGetMatchingServices")]
    internal static partial int Services(uint port, nint matching, out uint iterator);
    [LibraryImport(Io, EntryPoint = "IOIteratorNext")]
    internal static partial uint Next(uint iterator);
    [LibraryImport(Io, EntryPoint = "IORegistryEntryGetRegistryEntryID")]
    internal static partial int Identity(uint service, out ulong identity);
    [LibraryImport(Cf, EntryPoint = "CFNumberGetValue")]
    [return: MarshalAs(UnmanagedType.I1)]
    internal static partial bool Number(nint number, int type, out long value);
}
