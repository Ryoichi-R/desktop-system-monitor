using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace DesktopSystemMonitor.Windows.Processes;

internal static partial class ProcessIoInterop
{
    [StructLayout(LayoutKind.Sequential)]
    internal struct IoCounters
    {
        public ulong ReadOperationCount;
        public ulong WriteOperationCount;
        public ulong OtherOperationCount;
        public ulong ReadTransferCount;
        public ulong WriteTransferCount;
        public ulong OtherTransferCount;
    }

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool GetProcessIoCounters(SafeProcessHandle process, out IoCounters counters);
}
