using System.Runtime.InteropServices;

namespace DesktopSystemMonitor.Windows.Network;

internal static partial class IpHelperInterop
{
    // IfOperStatusUp
    public const int IF_OPER_STATUS_UP = 1;
    // IF_TYPE_SOFTWARE_LOOPBACK
    public const uint IF_TYPE_SOFTWARE_LOOPBACK = 24;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public unsafe struct MIB_IF_ROW2
    {
        public ulong InterfaceLuid;
        public uint InterfaceIndex;
        public Guid InterfaceGuid;
        public fixed char Alias[257];
        public fixed char Description[257];
        public uint PhysicalAddressLength;
        public fixed byte PhysicalAddress[32];
        public fixed byte PermanentPhysicalAddress[32];
        public uint Mtu;
        public uint Type;
        public int TunnelType;
        public int MediaType;
        public int PhysicalMediumType;
        public int AccessType;
        public int DirectionType;
        public byte InterfaceAndOperStatusFlags;
        // Compiler inserts 3 bytes of padding here so the next 4-byte field is aligned.
        public int OperStatus;
        public int AdminStatus;
        public int MediaConnectState;
        public Guid NetworkGuid;
        public int ConnectionType;
        // 4 bytes padding to 8-byte alignment for the ULONG64 counters below.
        public ulong TransmitLinkSpeed;
        public ulong ReceiveLinkSpeed;
        public ulong InOctets;
        public ulong InUcastPkts;
        public ulong InNUcastPkts;
        public ulong InDiscards;
        public ulong InErrors;
        public ulong InUnknownProtos;
        public ulong InUcastOctets;
        public ulong InMulticastOctets;
        public ulong InBroadcastOctets;
        public ulong OutOctets;
        public ulong OutUcastPkts;
        public ulong OutNUcastPkts;
        public ulong OutDiscards;
        public ulong OutErrors;
        public ulong OutUcastOctets;
        public ulong OutMulticastOctets;
        public ulong OutBroadcastOctets;
        public ulong OutQLen;

        public string GetAlias()
        {
            fixed (char* p = Alias)
            {
                return new string(p);
            }
        }
    }

    [LibraryImport("iphlpapi.dll")]
    public static partial uint GetIfTable2(out IntPtr Table);

    [LibraryImport("iphlpapi.dll")]
    public static partial void FreeMibTable(IntPtr Memory);

    public static unsafe IReadOnlyList<MIB_IF_ROW2> ReadAll()
    {
        uint result = GetIfTable2(out IntPtr ptr);
        if (result != 0 || ptr == IntPtr.Zero)
        {
            return Array.Empty<MIB_IF_ROW2>();
        }
        try
        {
            // MIB_IF_TABLE2 layout: ULONG NumEntries at offset 0, then 4 bytes
            // padding to 8-byte alignment, then MIB_IF_ROW2[NumEntries].
            uint numEntries = (uint)Marshal.ReadInt32(ptr);
            int rowSize = sizeof(MIB_IF_ROW2);
            var rows = new List<MIB_IF_ROW2>((int)numEntries);
            IntPtr rowBase = IntPtr.Add(ptr, 8);
            for (uint i = 0; i < numEntries; i++)
            {
                IntPtr rowPtr = IntPtr.Add(rowBase, checked((int)(i * (uint)rowSize)));
                var row = Marshal.PtrToStructure<MIB_IF_ROW2>(rowPtr);
                rows.Add(row);
            }
            return rows;
        }
        finally
        {
            FreeMibTable(ptr);
        }
    }
}
