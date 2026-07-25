using System.Runtime.InteropServices;

namespace DesktopSystemMonitor.Windows.Cpu;

internal static partial class ProcessorPowerInformation
{
    private const int ProcessorInformation = 11;

    [StructLayout(LayoutKind.Sequential)]
    private struct PROCESSOR_POWER_INFORMATION
    {
        public uint Number;
        public uint MaxMhz;
        public uint CurrentMhz;
        public uint MhzLimit;
        public uint MaxIdleState;
        public uint CurrentIdleState;
    }

    [LibraryImport("powrprof.dll", EntryPoint = "CallNtPowerInformation")]
    private static partial int CallNtPowerInformation(
        int InformationLevel,
        IntPtr InputBuffer,
        uint InputBufferLength,
        IntPtr OutputBuffer,
        uint OutputBufferLength);

    [LibraryImport("kernel32.dll")]
    private static partial uint GetActiveProcessorCount(ushort GroupNumber);

    /// <summary>
    /// Returns the MaxMhz value of logical processor 0. Returns 0 on failure —
    /// callers should treat that as "unknown" and fall back to a different
    /// frequency source.
    /// </summary>
    public static uint GetMaxMhz()
        => ReadAverage(info => info.MaxMhz);

    public static uint GetCurrentMhz()
        => ReadAverage(info => info.CurrentMhz);

    private static uint ReadAverage(Func<PROCESSOR_POWER_INFORMATION, uint> selector)
    {
        const ushort allProcessorGroups = 0xFFFF;
        uint activeCount = GetActiveProcessorCount(allProcessorGroups);
        int count = activeCount is > 0 and <= int.MaxValue
            ? (int)activeCount
            : Environment.ProcessorCount;
        if (count <= 0)
        {
            return 0;
        }
        int size = Marshal.SizeOf<PROCESSOR_POWER_INFORMATION>();
        IntPtr buffer = Marshal.AllocHGlobal(size * count);
        try
        {
            int status = CallNtPowerInformation(
                ProcessorInformation,
                IntPtr.Zero,
                0,
                buffer,
                (uint)(size * count));
            if (status != 0)
            {
                return 0;
            }
            ulong total = 0;
            uint valid = 0;
            for (int i = 0; i < count; i++)
            {
                var info = Marshal.PtrToStructure<PROCESSOR_POWER_INFORMATION>(IntPtr.Add(buffer, i * size));
                uint value = selector(info);
                if (value == 0)
                {
                    continue;
                }
                total += value;
                valid++;
            }
            return valid == 0 ? 0 : (uint)(total / valid);
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }
}
