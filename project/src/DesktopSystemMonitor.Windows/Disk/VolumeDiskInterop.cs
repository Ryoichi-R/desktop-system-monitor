using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace DesktopSystemMonitor.Windows.Disk;

internal static partial class VolumeDiskInterop
{
    [LibraryImport("kernel32.dll", EntryPoint = "CreateFileW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    internal static partial SafeFileHandle CreateFile(
        string fileName,
        uint desiredAccess,
        uint shareMode,
        IntPtr securityAttributes,
        uint creationDisposition,
        uint flags,
        IntPtr templateFile);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool DeviceIoControl(
        SafeFileHandle device,
        uint controlCode,
        IntPtr input,
        int inputSize,
        IntPtr output,
        int outputSize,
        out int bytesReturned,
        IntPtr overlapped);
}
