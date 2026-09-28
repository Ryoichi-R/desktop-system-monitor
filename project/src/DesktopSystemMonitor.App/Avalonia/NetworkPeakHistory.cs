using DesktopSystemMonitor.Core.Platform;
using DesktopSystemMonitor.Core.Sampling;
using DesktopSystemMonitor.Core.Formatting;

namespace DesktopSystemMonitor.App;

internal sealed class NetworkPeakHistory
{
    private RecentPeakTracker _receive = new();
    private RecentPeakTracker _send = new();
    private string? _identity;
    private DateTimeOffset? _last;
    internal int Seconds { get; private set; } = 60;
    internal string Receive { get; private set; } = "--";
    internal string Send { get; private set; } = "--";

    internal void SetWindow(int seconds)
    {
        if (seconds is < 10 or > 60) throw new ArgumentOutOfRangeException(nameof(seconds));
        if (seconds == Seconds) return;
        Seconds = seconds;
        _receive = new(TimeSpan.FromSeconds(seconds));
        _send = new(TimeSpan.FromSeconds(seconds));
        Clear();
    }

    internal void Update(SensorHostMetricGroup<SensorHostNetworkInterfaceReading> group, DateTimeOffset now)
    {
        if (group.Status != SensorHostMetricStatus.Ok || group.Values.Length == 0) { Clear(); return; }
        string identity = string.Join(";", group.Values.OrderBy(v => v.InterfaceIndex).Select(v => $"{v.InterfaceIndex}:{v.Name}"));
        if (identity != _identity || _last is { } last && (now < last || now - last > TimeSpan.FromSeconds(3))) Clear();
        _identity = identity;
        double receive = 0, send = 0;
        foreach (var item in group.Values)
        {
            if (item.ReceiveBytesPerSecond.Status != SensorHostMetricStatus.Ok || item.SendBytesPerSecond.Status != SensorHostMetricStatus.Ok
                || item.ReceiveBytesPerSecond.Value is not double rx || item.SendBytesPerSecond.Value is not double tx
                || !double.IsFinite(rx) || !double.IsFinite(tx) || rx < 0 || tx < 0) { Clear(); return; }
            receive += rx;
            send += tx;
        }
        if (!double.IsFinite(receive) || !double.IsFinite(send)) { Clear(); return; }
        _receive.Add(now, receive);
        _send.Add(now, send);
        _last = now;
        Receive = BytesPerSecondFormatter.Format(_receive.Peak);
        Send = BytesPerSecondFormatter.Format(_send.Peak);
    }

    private void Clear()
    {
        _receive.Clear();
        _send.Clear();
        _identity = null;
        _last = null;
        Receive = Send = "--";
    }
}
