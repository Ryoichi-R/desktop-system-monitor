namespace DesktopSystemMonitor.Core.Metrics;

public interface IGpuMetricSource : IMetricSource<GpuSnapshot>
{
    void SetPreferredAdapter(ulong? luid);
}
