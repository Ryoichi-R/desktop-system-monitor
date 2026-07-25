namespace DesktopSystemMonitor.Core.Sampling;

public sealed class RecentPeakTracker(TimeSpan? window = null)
{
    private readonly TimeSpan _window = window ?? TimeSpan.FromSeconds(60);
    private readonly Queue<(DateTimeOffset TakenAt, double Value)> _samples = new();
    private DateTimeOffset? _lastTakenAt;

    public double Peak => _samples.Count == 0 ? double.NaN : _samples.Max(sample => sample.Value);

    public bool Add(DateTimeOffset takenAt, double value)
    {
        if (!double.IsFinite(value) || _lastTakenAt is not null && takenAt < _lastTakenAt)
        {
            return false;
        }
        _lastTakenAt = takenAt;
        _samples.Enqueue((takenAt, value));
        Trim(takenAt);
        return true;
    }

    public void Clear()
    {
        _samples.Clear();
        _lastTakenAt = null;
    }

    private void Trim(DateTimeOffset now)
    {
        DateTimeOffset cutoff = now - _window;
        while (_samples.TryPeek(out var sample) && sample.TakenAt < cutoff)
        {
            _samples.Dequeue();
        }
    }
}
