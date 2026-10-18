using System.Runtime.InteropServices;

namespace DesktopSystemMonitor.Mac.SensorHost.Interop;

internal static unsafe partial class ProcessNative
{
    private const string System = "/usr/lib/libSystem.B.dylib";
    [LibraryImport(System, EntryPoint = "proc_listpids")]
    internal static partial int List(uint type, uint typeInfo, int* buffer, int size);
    [LibraryImport(System, EntryPoint = "proc_pid_rusage")]
    internal static partial int Usage(int pid, int flavor, byte* buffer);
    [LibraryImport(System, EntryPoint = "proc_name")]
    internal static partial int Name(int pid, byte* buffer, uint size);
    [LibraryImport(System, EntryPoint = "mach_timebase_info")]
    internal static partial int Timebase(out TimebaseInfo info);
    [StructLayout(LayoutKind.Sequential)]
    internal struct TimebaseInfo { internal uint Numerator, Denominator; }
}
