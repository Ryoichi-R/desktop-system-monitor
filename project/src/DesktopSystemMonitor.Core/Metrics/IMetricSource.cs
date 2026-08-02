namespace DesktopSystemMonitor.Core.Metrics;

public interface IMetricSource<TSnapshot> : IDisposable
{
    ValueTask<TSnapshot> SampleAsync(CancellationToken cancellationToken);
    void ResetBaseline();
}
