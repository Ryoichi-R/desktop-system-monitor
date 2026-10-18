namespace DesktopSystemMonitor.Core.Metrics;

public interface IDiskMetricSource : IMetricSource<DiskSnapshot>
{
    void SetEnabled(bool enabled, int? selectedDiskNumber);
}
