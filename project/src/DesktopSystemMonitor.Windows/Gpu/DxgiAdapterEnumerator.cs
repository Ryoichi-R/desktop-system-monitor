using System.Runtime.InteropServices;

namespace DesktopSystemMonitor.Windows.Gpu;

/// <summary>
/// Enumerates DXGI adapters via CreateDXGIFactory1 and calls
/// IDXGIAdapter1::GetDesc1 to retrieve the per-adapter description including
/// AdapterLuid and DedicatedVideoMemory.
///
/// COM invocation is done through raw function pointers on the vtable rather
/// than declared <see cref="ComImportAttribute"/> interfaces, so we don't have
/// to fully mirror every DXGI vtable slot we don't use.
/// </summary>
public static unsafe partial class DxgiAdapterEnumerator
{
    private const int VTBL_RELEASE = 2;
    private const int VTBL_ENUM_ADAPTERS1 = 12;
    private const int VTBL_ADAPTER1_GET_DESC1 = 10;
    private const uint DXGI_ADAPTER_FLAG_SOFTWARE = 2;
    private static readonly Guid IID_IDXGIFactory1 = new("770aae78-f26f-4dba-a829-253c83d1b387");
    private const int DXGI_ERROR_NOT_FOUND = unchecked((int)0x887A0002);

    [StructLayout(LayoutKind.Sequential)]
    private unsafe struct DXGI_ADAPTER_DESC1
    {
        // The C header defines Description as WCHAR[128]. C# needs an inline
        // fixed buffer; we later copy out into a managed string.
        public fixed char Description[128];
        public uint VendorId;
        public uint DeviceId;
        public uint SubSysId;
        public uint Revision;
        public nuint DedicatedVideoMemory;
        public nuint DedicatedSystemMemory;
        public nuint SharedSystemMemory;
        public uint LuidLow;
        public int LuidHigh;
        public uint Flags;
    }

    [LibraryImport("dxgi.dll")]
    private static partial int CreateDXGIFactory1(in Guid riid, out IntPtr ppFactory);

    public static IReadOnlyList<DxgiAdapterInfo> Enumerate()
    {
        int hr = CreateDXGIFactory1(in IID_IDXGIFactory1, out IntPtr factory);
        if (hr < 0 || factory == IntPtr.Zero)
        {
            return Array.Empty<DxgiAdapterInfo>();
        }
        var results = new List<DxgiAdapterInfo>();
        try
        {
            IntPtr* factoryVtbl = *(IntPtr**)factory;
            var enumAdapters1 = (delegate* unmanaged[Stdcall]<IntPtr, uint, IntPtr*, int>)factoryVtbl[VTBL_ENUM_ADAPTERS1];

            for (uint i = 0; ; i++)
            {
                IntPtr adapter;
                int enumHr = enumAdapters1(factory, i, &adapter);
                if (enumHr == DXGI_ERROR_NOT_FOUND || enumHr < 0 || adapter == IntPtr.Zero)
                {
                    break;
                }
                try
                {
                    IntPtr* adapterVtbl = *(IntPtr**)adapter;
                    var getDesc1 = (delegate* unmanaged[Stdcall]<IntPtr, DXGI_ADAPTER_DESC1*, int>)adapterVtbl[VTBL_ADAPTER1_GET_DESC1];
                    DXGI_ADAPTER_DESC1 desc;
                    int descHr = getDesc1(adapter, &desc);
                    if (descHr < 0)
                    {
                        continue;
                    }
                    string description = new(desc.Description);
                    ulong luid = ((ulong)(uint)desc.LuidHigh << 32) | desc.LuidLow;
                    bool isSoftware = (desc.Flags & DXGI_ADAPTER_FLAG_SOFTWARE) != 0;
                    results.Add(new DxgiAdapterInfo(
                        Luid: luid,
                        Description: description,
                        DedicatedVideoMemoryBytes: unchecked((long)(ulong)desc.DedicatedVideoMemory),
                        DedicatedSystemMemoryBytes: unchecked((long)(ulong)desc.DedicatedSystemMemory),
                        SharedSystemMemoryBytes: unchecked((long)(ulong)desc.SharedSystemMemory),
                        IsSoftware: isSoftware));
                }
                finally
                {
                    IntPtr* adapterVtbl = *(IntPtr**)adapter;
                    var release = (delegate* unmanaged[Stdcall]<IntPtr, uint>)adapterVtbl[VTBL_RELEASE];
                    _ = release(adapter);
                }
            }
        }
        finally
        {
            IntPtr* factoryVtbl = *(IntPtr**)factory;
            var release = (delegate* unmanaged[Stdcall]<IntPtr, uint>)factoryVtbl[VTBL_RELEASE];
            _ = release(factory);
        }
        return results;
    }
}
