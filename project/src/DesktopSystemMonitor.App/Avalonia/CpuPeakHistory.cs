using DesktopSystemMonitor.Core.Platform;
using DesktopSystemMonitor.Core.Sampling;
namespace DesktopSystemMonitor.App;

internal sealed class CpuPeakHistory
{
    private RecentPeakTracker _tracker = new();
    private DateTimeOffset? _last;
    internal int Seconds { get; private set; } = 60;
    internal string Text { get; private set; } = "60s max --";
    internal void SetWindow(int seconds)
    {
        if (seconds is < 10 or > 60) throw new ArgumentOutOfRangeException(nameof(seconds));
        if (Seconds == seconds) return;
        Seconds = seconds;
        _tracker = new(TimeSpan.FromSeconds(seconds));
        _last = null;
        Text = $"{Seconds}s max --";
    }
    internal void Update(SensorHostMetricValue metric, DateTimeOffset now)
    {
        if (_last is { } last && (now < last || now - last > TimeSpan.FromSeconds(3))) _tracker.Clear();
        _last = now;
        if (metric.Status != SensorHostMetricStatus.Ok || metric.Value is not double value || !double.IsFinite(value) || value is < 0 or > 100)
        { _tracker.Clear(); Text = $"{Seconds}s max --"; return; }
        _tracker.Add(now, value);
        Text = $"{Seconds}s max {_tracker.Peak:0}%";
    }
}
