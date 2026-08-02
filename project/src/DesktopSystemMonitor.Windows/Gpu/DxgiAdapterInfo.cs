namespace DesktopSystemMonitor.Windows.Gpu;

public sealed record DxgiAdapterInfo(
    ulong Luid,
    string Description,
    long DedicatedVideoMemoryBytes,
    long DedicatedSystemMemoryBytes,
    long SharedSystemMemoryBytes,
    bool IsSoftware);
