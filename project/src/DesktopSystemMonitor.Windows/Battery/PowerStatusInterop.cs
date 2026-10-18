using System.Runtime.InteropServices;

namespace DesktopSystemMonitor.Windows.Battery;

internal static partial class PowerStatusInterop
{
    internal const int SystemBatteryState = 5;

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    internal struct SystemPowerStatus
    {
        public byte AcLineStatus;
        public byte BatteryFlag;
        public byte BatteryLifePercent;
        public byte SystemStatusFlag;
        public uint BatteryLifeTime;
        public uint BatteryFullLifeTime;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    internal struct SystemBatteryStateValue
    {
        public byte AcOnLine;
        public byte BatteryPresent;
        public byte Charging;
        public byte Discharging;
        public byte Spare0;
        public byte Spare1;
        public byte Spare2;
        public byte Tag;
        public uint MaxCapacity;
        public uint RemainingCapacity;
        public int Rate;
        public uint EstimatedTime;
        public uint DefaultAlert1;
        public uint DefaultAlert2;
    }

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool GetSystemPowerStatus(out SystemPowerStatus status);

    [LibraryImport("powrprof.dll")]
    internal static partial uint CallNtPowerInformation(int informationLevel, IntPtr inputBuffer, uint inputBufferLength, out SystemBatteryStateValue outputBuffer, uint outputBufferLength);
}
