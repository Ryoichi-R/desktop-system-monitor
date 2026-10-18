using System.Runtime.InteropServices;
namespace DesktopSystemMonitor.Mac.SensorHost.Interop;

internal static unsafe partial class ThermalNative
{
    private const string Io = "/System/Library/Frameworks/IOKit.framework/IOKit";
    private const string Cf = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";
    private const string Report = "/usr/lib/libIOReport.dylib";
    [LibraryImport(Io, EntryPoint = "IOServiceOpen")]
    internal static partial int Open(uint service, uint task, uint type, out uint connection);
    [LibraryImport(Io, EntryPoint = "IOServiceClose")]
    internal static partial int Close(uint connection);
    [LibraryImport(Io, EntryPoint = "IOConnectCallStructMethod")]
    internal static partial int Call(uint connection, uint selector, byte* input, nuint inputSize, byte* output, ref nuint outputSize);
    [LibraryImport(Cf, EntryPoint = "CFStringGetCString")]
    [return: MarshalAs(UnmanagedType.I1)]
    internal static partial bool GetString(nint text, byte* buffer, nint size, uint encoding);
    [LibraryImport(Cf, EntryPoint = "CFStringGetTypeID")]
    internal static partial nuint StringType();
    [LibraryImport(Cf, EntryPoint = "CFArrayGetTypeID")]
    internal static partial nuint ArrayType();
    [LibraryImport(Cf, EntryPoint = "CFArrayGetCount")]
    internal static partial nint Count(nint array);
    [LibraryImport(Cf, EntryPoint = "CFArrayGetValueAtIndex")]
    internal static partial nint At(nint array, nint index);
    [LibraryImport(Cf, EntryPoint = "CFPropertyListCreateDeepCopy")]
    internal static partial nint Copy(nint allocator, nint value, nuint options);
    [LibraryImport(Cf, EntryPoint = "CFArrayRemoveValueAtIndex")]
    internal static partial void Remove(nint array, nint index);
    [LibraryImport(Report, EntryPoint = "IOReportCopyChannelsInGroup")]
    internal static partial nint Channels(nint group, nint subgroup, ulong a, ulong b, ulong c);
    [LibraryImport(Report, EntryPoint = "IOReportCreateSubscription")]
    internal static partial nint Subscribe(nint allocator, nint channels, out nint subscribed, ulong channelId, nint options);
    [LibraryImport(Report, EntryPoint = "IOReportCreateSamples")]
    internal static partial nint Samples(nint subscription, nint channels, nint options);
    [LibraryImport(Report, EntryPoint = "IOReportCreateSamplesDelta")]
    internal static partial nint Delta(nint previous, nint current, nint options);
    [LibraryImport(Report, EntryPoint = "IOReportChannelGetChannelName")]
    internal static partial nint ChannelName(nint channel);
    [LibraryImport(Report, EntryPoint = "IOReportChannelGetUnitLabel")]
    internal static partial nint Unit(nint channel);
    [LibraryImport(Report, EntryPoint = "IOReportSimpleGetIntegerValue")]
    internal static partial long Integer(nint channel, int index);
}
