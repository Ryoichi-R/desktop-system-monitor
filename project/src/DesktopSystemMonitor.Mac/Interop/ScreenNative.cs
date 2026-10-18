using System.Runtime.InteropServices;

namespace DesktopSystemMonitor.Mac.Interop;

[StructLayout(LayoutKind.Sequential)]
internal struct ScreenRect { internal double X, Y, Width, Height; }

internal static partial class ScreenNative
{
    private const string Cg = "/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics";
    private const string Cf = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";
    [LibraryImport(Cg, EntryPoint = "CGWindowListCopyWindowInfo")]
    internal static partial nint Windows(uint options, uint relativeTo);
    [LibraryImport(Cg, EntryPoint = "CGDisplayBounds")]
    internal static partial ScreenRect DisplayBounds(uint display);
    [LibraryImport(Cg, EntryPoint = "CGRectMakeWithDictionaryRepresentation")]
    [return: MarshalAs(UnmanagedType.I1)]
    internal static partial bool Rectangle(nint dictionary, out ScreenRect rectangle);
    [LibraryImport(Cf, EntryPoint = "CFStringCreateWithCString", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial nint String(nint allocator, string text, uint encoding);
    [LibraryImport(Cf, EntryPoint = "CFRelease")]
    internal static partial void Release(nint value);
    [LibraryImport(Cf, EntryPoint = "CFGetTypeID")]
    internal static partial nuint Type(nint value);
    [LibraryImport(Cf, EntryPoint = "CFArrayGetTypeID")]
    internal static partial nuint ArrayType();
    [LibraryImport(Cf, EntryPoint = "CFDictionaryGetTypeID")]
    internal static partial nuint DictionaryType();
    [LibraryImport(Cf, EntryPoint = "CFNumberGetTypeID")]
    internal static partial nuint NumberType();
    [LibraryImport(Cf, EntryPoint = "CFArrayGetCount")]
    internal static partial nint Count(nint array);
    [LibraryImport(Cf, EntryPoint = "CFArrayGetValueAtIndex")]
    internal static partial nint At(nint array, nint index);
    [LibraryImport(Cf, EntryPoint = "CFDictionaryGetValue")]
    internal static partial nint Value(nint dictionary, nint key);
    [LibraryImport(Cf, EntryPoint = "CFNumberGetValue")]
    [return: MarshalAs(UnmanagedType.I1)]
    internal static partial bool Number(nint number, int type, out double value);
    [LibraryImport("/usr/lib/libobjc.A.dylib", EntryPoint = "sel_registerName", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial nint Selector(string name);
    [LibraryImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    internal static partial nint Send(nint receiver, nint selector);
    [LibraryImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    internal static partial nint SendObject(nint receiver, nint selector, nint value);
}
