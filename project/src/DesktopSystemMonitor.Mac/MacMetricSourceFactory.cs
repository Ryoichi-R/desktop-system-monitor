using DesktopSystemMonitor.Core.Metrics;

namespace DesktopSystemMonitor.Mac;

/// <summary>
/// macOS実機スパイク完了前のfail-safe composition。未実装または実機依存の値を
/// 推測せずUnavailableへ落とし、バッテリーだけはMac Studioの仕様からAbsentを返す。
/// </summary>
public sealed class MacMetricSourceFactory : IMetricSourceFactory
{
    public IMetricSource<CpuSnapshot> CreateCpu() =>
        new UnavailableMetricSource<CpuSnapshot>(CpuSnapshot.Unavailable);

    public IMetricSource<MemorySnapshot> CreateMemory() =>
        new UnavailableMetricSource<MemorySnapshot>(MemorySnapshot.Unavailable);

    public IGpuMetricSource CreateGpu() => new UnavailableGpuMetricSource();

    public INetworkMetricSource CreateNetwork() => new UnavailableNetworkMetricSource();

    public IPowerMetricSource CreatePower(Action<string, Exception> onDiagnostic) =>
        new UnavailablePowerMetricSource();

    public IDiskMetricSource CreateDisk() => new UnavailableDiskMetricSource();

    public IBatteryMetricSource CreateBattery() => new MacBatteryMetricSource();

    private class UnavailableMetricSource<TSnapshot>(Func<TSnapshot> createSnapshot) : IMetricSource<TSnapshot>
    {
        public ValueTask<TSnapshot> SampleAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(createSnapshot());
        }

        public void ResetBaseline()
        {
        }

        public void Dispose()
        {
        }
    }

    private sealed class UnavailableGpuMetricSource : UnavailableMetricSource<GpuSnapshot>, IGpuMetricSource
    {
        public UnavailableGpuMetricSource()
            : base(GpuSnapshot.Unavailable)
        {
        }

        public void SetPreferredAdapter(ulong? luid)
        {
        }
    }

    private sealed class UnavailableNetworkMetricSource : UnavailableMetricSource<NetworkSnapshot>, INetworkMetricSource
    {
        public UnavailableNetworkMetricSource()
            : base(NetworkSnapshot.Unavailable)
        {
        }

        public void SetSelectedAdapters(IEnumerable<ulong> luids)
        {
        }
    }

    private sealed class UnavailablePowerMetricSource : UnavailableMetricSource<PowerSnapshot>, IPowerMetricSource
    {
        public UnavailablePowerMetricSource()
            : base(PowerSnapshot.Unavailable)
        {
        }

        public void PausePolling()
        {
        }

        public void ResumePolling()
        {
        }
    }

    private sealed class UnavailableDiskMetricSource : UnavailableMetricSource<DiskSnapshot>, IDiskMetricSource
    {
        public UnavailableDiskMetricSource()
            : base(() => DiskSnapshot.Unavailable())
        {
        }

        public void SetEnabled(bool enabled, int? selectedDiskNumber)
        {
        }
    }
}
