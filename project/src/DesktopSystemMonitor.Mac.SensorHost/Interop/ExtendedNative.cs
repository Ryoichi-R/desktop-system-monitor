using System.Runtime.InteropServices;

namespace DesktopSystemMonitor.Mac.SensorHost.Interop;

internal static unsafe partial class ExtendedNative
{
    private const string System = "/usr/lib/libSystem.B.dylib";
    private const string Io = "/System/Library/Frameworks/IOKit.framework/IOKit";
    private const string Cf = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";
    [LibraryImport(System, EntryPoint = "sysctl")]
    internal static partial int Sysctl(int* name, uint count, byte* buffer, ref nuint length, nint newValue, nuint newLength);
    [LibraryImport(System, EntryPoint = "if_indextoname")]
    internal static partial nint InterfaceName(uint index, byte* name);
    [LibraryImport(Io, EntryPoint = "IOServiceMatching", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial nint Matching(string name);
    [LibraryImport(Io, EntryPoint = "IOServiceGetMatchingService")]
    internal static partial uint Service(uint port, nint matching);
    [LibraryImport(Io, EntryPoint = "IOObjectRelease")]
    internal static partial int ReleaseObject(uint value);
    [LibraryImport(Io, EntryPoint = "IORegistryEntryCreateCFProperty")]
    internal static partial nint Property(uint entry, nint key, nint allocator, uint options);
    [LibraryImport(Cf, EntryPoint = "CFStringCreateWithCString", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial nint String(nint allocator, string text, uint encoding);
    [LibraryImport(Cf, EntryPoint = "CFRelease")]
    internal static partial void Release(nint value);
    [LibraryImport(Cf, EntryPoint = "CFGetTypeID")]
    internal static partial nuint TypeId(nint value);
    [LibraryImport(Cf, EntryPoint = "CFDictionaryGetTypeID")]
    internal static partial nuint DictionaryType();
    [LibraryImport(Cf, EntryPoint = "CFNumberGetTypeID")]
    internal static partial nuint NumberType();
    [LibraryImport(Cf, EntryPoint = "CFDictionaryGetValue")]
    internal static partial nint DictionaryValue(nint dictionary, nint key);
    [LibraryImport(Cf, EntryPoint = "CFNumberGetValue")]
    [return: MarshalAs(UnmanagedType.I1)]
    internal static partial bool NumberValue(nint number, int type, out double value);
}
