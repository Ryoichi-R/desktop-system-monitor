using DesktopSystemMonitor.Core.Metrics;

namespace DesktopSystemMonitor.Windows.Memory;

/// <summary>Reads physical memory totals from the Windows GlobalMemoryStatusEx API.</summary>
public sealed class MemoryMetricSource : IMetricSource<MemorySnapshot>
{
    private readonly Func<MemoryReading?> _read;

    public MemoryMetricSource()
        : this(MemoryStatusInterop.Read)
    {
    }

    internal MemoryMetricSource(Func<MemoryReading?> read)
    {
        _read = read;
    }

    public ValueTask<MemorySnapshot> SampleAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        MemoryReading? reading = _read();
        if (reading is not { } value
            || value.TotalPhysicalBytes == 0
            || value.AvailablePhysicalBytes > value.TotalPhysicalBytes
            || value.TotalPhysicalBytes > long.MaxValue)
        {
            return ValueTask.FromResult(MemorySnapshot.Unavailable());
        }

        ulong used = value.TotalPhysicalBytes - value.AvailablePhysicalBytes;
        double utilization = used * 100d / value.TotalPhysicalBytes;
        return ValueTask.FromResult(new MemorySnapshot
        {
            Status = MetricStatus.Ok,
            UtilizationPercent = Math.Clamp(utilization, 0d, 100d),
            UsedBytes = (long)used,
            TotalBytes = (long)value.TotalPhysicalBytes,
        });
    }

    public void ResetBaseline()
    {
    }

    public void Dispose()
    {
    }
}
