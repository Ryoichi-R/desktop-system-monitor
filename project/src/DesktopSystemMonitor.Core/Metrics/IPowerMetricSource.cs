namespace DesktopSystemMonitor.Core.Metrics;

public interface IPowerMetricSource : IMetricSource<PowerSnapshot>
{
    void PausePolling();
    void ResumePolling();
}
