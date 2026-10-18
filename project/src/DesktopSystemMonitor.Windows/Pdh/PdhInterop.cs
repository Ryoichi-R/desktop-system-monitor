using System.Runtime.InteropServices;

namespace DesktopSystemMonitor.Windows.Pdh;

internal static partial class PdhInterop
{
    public const uint PDH_CSTATUS_VALID_DATA = 0;
    public const uint PDH_CSTATUS_NEW_DATA = 1;
    public const uint PDH_FMT_DOUBLE = 0x0200;
    public const uint PDH_FMT_LARGE = 0x0400;
    public const uint PDH_FMT_NOSCALE = 0x1000;
    public const uint PDH_FMT_NOCAP100 = 0x8000;
    public const uint PDH_REFRESHCOUNTERS = 0x00000001;

    [StructLayout(LayoutKind.Explicit)]
    public struct PDH_FMT_COUNTERVALUE
    {
        [FieldOffset(0)] public uint CStatus;
        [FieldOffset(8)] public double doubleValue;
        [FieldOffset(8)] public long largeValue;
    }

    [LibraryImport("pdh.dll", EntryPoint = "PdhOpenQueryW")]
    public static partial uint PdhOpenQuery(IntPtr szDataSource, IntPtr dwUserData, out IntPtr phQuery);

    [LibraryImport("pdh.dll")]
    public static partial uint PdhCloseQuery(IntPtr hQuery);

    [LibraryImport("pdh.dll", EntryPoint = "PdhAddEnglishCounterW", StringMarshalling = StringMarshalling.Utf16)]
    public static partial uint PdhAddEnglishCounter(SafePdhQueryHandle hQuery, string szFullCounterPath, IntPtr dwUserData, out IntPtr phCounter);

    [LibraryImport("pdh.dll")]
    public static partial uint PdhRemoveCounter(IntPtr hCounter);

    [LibraryImport("pdh.dll")]
    public static partial uint PdhCollectQueryData(SafePdhQueryHandle hQuery);

    [LibraryImport("pdh.dll")]
    public static partial uint PdhGetFormattedCounterValue(IntPtr hCounter, uint dwFormat, out uint lpdwType, out PDH_FMT_COUNTERVALUE pValue);

    [LibraryImport("pdh.dll", EntryPoint = "PdhExpandWildCardPathW", StringMarshalling = StringMarshalling.Utf16)]
    public static partial uint PdhExpandWildCardPath(IntPtr szDataSource, string szWildCardPath, IntPtr mszExpandedPathList, ref uint pcchPathListLength, uint dwFlags);
}
