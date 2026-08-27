namespace DesktopSystemMonitor.Core.Metrics;

public interface INetworkMetricSource : IMetricSource<NetworkSnapshot>
{
    void SetSelectedAdapters(IEnumerable<ulong> luids);
}
