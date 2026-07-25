using DesktopSystemMonitor.Core.Metrics;
using DesktopSystemMonitor.Windows.Pdh;

namespace DesktopSystemMonitor.Windows.Disk;

public interface IDiskMetricSource : IMetricSource<DiskSnapshot>
{
    void SetEnabled(bool enabled, int? selectedDiskNumber);
}

internal interface IDiskCounterQuery : IDisposable
{
    bool TryAddCounter(string path);
    bool Collect();
    bool TryGetDouble(string path, out double value);
}

internal sealed class PdhDiskCounterQuery : IDiskCounterQuery
{
    private readonly PdhQuery _query = new();

    public bool TryAddCounter(string path) => _query.TryAddCounter(path);
    public bool Collect() => _query.Collect();
    public bool TryGetDouble(string path, out double value) => _query.TryGetDouble(path, out value);
    public void Dispose() => _query.Dispose();
}

public sealed class DiskMetricSource : IDiskMetricSource
{
    private const string IdleWildcard = @"\PhysicalDisk(*)\% Idle Time";
    private static readonly TimeSpan RebuildRetryDelay = TimeSpan.FromSeconds(5);
    private readonly IVolumeDiskResolver _resolver;
    private readonly Func<IReadOnlyList<string>> _expandWildcard;
    private readonly Func<IDiskCounterQuery> _queryFactory;
    private readonly Func<DateTimeOffset> _utcNow;
    private IDiskCounterQuery? _query;
    private string? _idlePath;
    private string? _readPath;
    private string? _writePath;
    private int? _requestedDiskNumber;
    private int? _activeDiskNumber;
    private string _label = "自動選択不可";
    private DiskAvailabilityReason _reason = DiskAvailabilityReason.SystemDiskResolveFailed;
    private bool _enabled;
    private bool _warmed;
    private DateTimeOffset _nextRebuildAt = DateTimeOffset.MinValue;

    public DiskMetricSource() : this(
        new VolumeDiskResolver(),
        () => PdhQuery.ExpandWildcard(IdleWildcard),
        () => new PdhDiskCounterQuery(),
        () => DateTimeOffset.UtcNow)
    {
    }

    internal DiskMetricSource(IVolumeDiskResolver resolver) : this(
        resolver,
        () => PdhQuery.ExpandWildcard(IdleWildcard),
        () => new PdhDiskCounterQuery(),
        () => DateTimeOffset.UtcNow)
    {
    }

    internal DiskMetricSource(
        IVolumeDiskResolver resolver,
        Func<IReadOnlyList<string>> expandWildcard,
        Func<IDiskCounterQuery> queryFactory,
        Func<DateTimeOffset> utcNow)
    {
        _resolver = resolver;
        _expandWildcard = expandWildcard;
        _queryFactory = queryFactory;
        _utcNow = utcNow;
    }

    public static IReadOnlyList<int> EnumerateAvailableDiskNumbers() => PdhQuery.ExpandWildcard(IdleWildcard)
        .Select(path => DiskInstanceName.TryParseCounterPath(path, out PhysicalDiskInstance parsed) ? parsed.DiskNumber : (int?)null)
        .Where(number => number is not null)
        .Select(number => number!.Value)
        .Distinct()
        .Order()
        .ToArray();

    public void SetEnabled(bool enabled, int? selectedDiskNumber)
    {
        if (_enabled == enabled && _requestedDiskNumber == selectedDiskNumber)
        {
            return;
        }
        _enabled = enabled;
        _requestedDiskNumber = selectedDiskNumber;
        RebuildSafely();
    }

    public ValueTask<DiskSnapshot> SampleAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!_enabled)
        {
            return ValueTask.FromResult(DiskSnapshot.Unavailable());
        }
        if (_query is null && _utcNow() >= _nextRebuildAt)
        {
            RebuildSafely();
        }
        if (_query is null || _idlePath is null || _readPath is null || _writePath is null)
        {
            return ValueTask.FromResult(DiskSnapshot.Unavailable(_label, _reason));
        }
        bool collected;
        try
        {
            collected = _query.Collect();
        }
        catch
        {
            collected = false;
        }
        if (!collected)
        {
            string failedLabel = _label;
            RebuildSafely();
            return ValueTask.FromResult(DiskSnapshot.Unavailable(failedLabel, DiskAvailabilityReason.CollectionFailed));
        }
        if (!_warmed)
        {
            _warmed = true;
            return ValueTask.FromResult(DiskSnapshot.Warmup() with { SelectedDiskNumber = _activeDiskNumber, SelectedDiskLabel = _label });
        }
        double idle = double.NaN;
        double read = double.NaN;
        double write = double.NaN;
        bool readSucceeded;
        try
        {
            readSucceeded = _query.TryGetDouble(_idlePath, out idle) &&
                _query.TryGetDouble(_readPath, out read) &&
                _query.TryGetDouble(_writePath, out write);
        }
        catch
        {
            readSucceeded = false;
        }
        if (!readSucceeded || read < 0 || write < 0)
        {
            string failedLabel = _label;
            RebuildSafely();
            return ValueTask.FromResult(DiskSnapshot.Unavailable(failedLabel, DiskAvailabilityReason.CounterReadFailed));
        }
        return ValueTask.FromResult(new DiskSnapshot
        {
            Status = MetricStatus.Ok,
            ActivePercent = Math.Clamp(100d - idle, 0d, 100d),
            ReadBytesPerSecond = read,
            WriteBytesPerSecond = write,
            SelectedDiskNumber = _activeDiskNumber,
            SelectedDiskLabel = _label,
            AvailabilityReason = DiskAvailabilityReason.None,
        });
    }

    public void ResetBaseline() => _warmed = false;

    private void RebuildSafely()
    {
        try
        {
            Rebuild();
        }
        catch
        {
            _query?.Dispose();
            _query = null;
            _warmed = false;
            _activeDiskNumber = null;
            _reason = DiskAvailabilityReason.CollectionFailed;
            ScheduleRetry();
        }
    }

    private void Rebuild()
    {
        _query?.Dispose();
        _query = null;
        _warmed = false;
        _activeDiskNumber = null;
        if (!_enabled)
        {
            _nextRebuildAt = DateTimeOffset.MaxValue;
            return;
        }
        int? requested = _requestedDiskNumber;
        if (requested is null)
        {
            SystemDiskResolution resolution;
            try
            {
                resolution = _resolver.ResolveSystemDisk();
            }
            catch
            {
                resolution = SystemDiskResolution.Failed(DiskAvailabilityReason.SystemDiskResolveFailed);
            }
            if (!resolution.Succeeded)
            {
                _label = "自動選択不可";
                _reason = resolution.Reason;
                ScheduleRetry();
                return;
            }
            requested = resolution.DiskNumber;
        }
        if (requested is not int requestedDiskNumber)
        {
            _label = "自動選択不可";
            _reason = DiskAvailabilityReason.SystemDiskResolveFailed;
            ScheduleRetry();
            return;
        }

        IReadOnlyList<string> counterPaths;
        try
        {
            counterPaths = _expandWildcard();
        }
        catch
        {
            _label = $"Disk {requestedDiskNumber} N/A";
            _reason = DiskAvailabilityReason.PdhEnumerationFailed;
            ScheduleRetry();
            return;
        }

        PhysicalDiskInstance? match = counterPaths
            .Select(path => DiskInstanceName.TryParseCounterPath(path, out PhysicalDiskInstance parsed) ? parsed : (PhysicalDiskInstance?)null)
            .FirstOrDefault(item => item?.DiskNumber == requestedDiskNumber);
        if (match is null)
        {
            _label = $"Disk {requestedDiskNumber} N/A";
            _reason = DiskAvailabilityReason.RequestedDiskNotFound;
            ScheduleRetry();
            return;
        }
        _activeDiskNumber = requestedDiskNumber;
        _label = _requestedDiskNumber is null ? $"自動: Disk {requestedDiskNumber}" : $"手動: Disk {requestedDiskNumber}";
        _reason = DiskAvailabilityReason.None;
        string prefix = $@"\PhysicalDisk({match.Value.InstanceName})";
        _idlePath = prefix + @"\% Idle Time";
        _readPath = prefix + @"\Disk Read Bytes/sec";
        _writePath = prefix + @"\Disk Write Bytes/sec";
        IDiskCounterQuery? query = null;
        try
        {
            query = _queryFactory();
            if (!query.TryAddCounter(_idlePath) || !query.TryAddCounter(_readPath) || !query.TryAddCounter(_writePath))
            {
                query.Dispose();
                _reason = DiskAvailabilityReason.CounterOpenFailed;
                ScheduleRetry();
                return;
            }
        }
        catch
        {
            try
            {
                query?.Dispose();
            }
            catch
            {
                // The original counter-open failure determines the public reason.
            }
            _reason = DiskAvailabilityReason.CounterOpenFailed;
            ScheduleRetry();
            return;
        }
        _query = query;
        _nextRebuildAt = DateTimeOffset.MaxValue;
    }

    private void ScheduleRetry() => _nextRebuildAt = _utcNow() + RebuildRetryDelay;

    public void Dispose() => _query?.Dispose();
}
