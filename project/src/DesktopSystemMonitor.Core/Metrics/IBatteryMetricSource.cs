namespace DesktopSystemMonitor.Core.Metrics;

public interface IBatteryMetricSource : IMetricSource<BatterySnapshot>
{
    void SetEnabled(bool enabled);
}
