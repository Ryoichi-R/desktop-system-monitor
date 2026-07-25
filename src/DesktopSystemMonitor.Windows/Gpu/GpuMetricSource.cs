using DesktopSystemMonitor.Core.Metrics;
using DesktopSystemMonitor.Core.Utility;
using DesktopSystemMonitor.Windows.Pdh;

namespace DesktopSystemMonitor.Windows.Gpu;

/// <summary>
/// Aggregates the <c>GPU Engine(*)\Utilization Percentage</c> counters into a
/// per-adapter representative value using the busiest-engine rule, and matches
/// them with <c>GPU Adapter Memory(*)\Dedicated Usage</c> and DXGI
/// <c>DedicatedVideoMemory</c> by adapter LUID.
///
/// The wildcard instance set is refreshed on a fixed cadence to catch new
/// processes; when the LUID set from GPU Engine no longer matches DXGI's set
/// we re-run DXGI enumeration as well.
/// </summary>
public interface IGpuMetricSource : IMetricSource<GpuSnapshot>
{
    void SetPreferredAdapter(ulong? luid);
}

public sealed class GpuMetricSource : IGpuMetricSource
{
    private const string EngineWildcard = @"\GPU Engine(*)\Utilization Percentage";
    private const string MemoryWildcard = @"\GPU Adapter Memory(*)\Dedicated Usage";

    private readonly TimeSpan _refreshInterval;
    private readonly IReadOnlyList<DxgiAdapterInfo> _dxgiOverride;
    private readonly IClock _clock;
    private PdhQuery _query;
    private DateTimeOffset _lastRefreshUtc = DateTimeOffset.MinValue;
    private DateTimeOffset _lastDxgiEnumUtc = DateTimeOffset.MinValue;
    private IReadOnlyList<DxgiAdapterInfo> _dxgi = Array.Empty<DxgiAdapterInfo>();
    private int _samplesCollected;
    private readonly PreferredAdapterSelectionStore _preferredAdapter = new();
    private readonly GpuRecoveryTracker _recovery = new();
    private readonly GpuRecoveryTracker _memoryRecovery = new();

    internal int WildcardRefreshCount { get; private set; }

    public GpuMetricSource() : this(TimeSpan.FromSeconds(5), Array.Empty<DxgiAdapterInfo>(), new SystemClock())
    {
    }

    internal GpuMetricSource(TimeSpan refreshInterval, IReadOnlyList<DxgiAdapterInfo> dxgiOverride)
        : this(refreshInterval, dxgiOverride, new SystemClock())
    {
    }

    internal GpuMetricSource(
        TimeSpan refreshInterval,
        IReadOnlyList<DxgiAdapterInfo> dxgiOverride,
        IClock clock)
    {
        _refreshInterval = refreshInterval;
        _dxgiOverride = dxgiOverride;
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _query = new PdhQuery();
        RefreshWildcards(force: true);
        _lastRefreshUtc = _clock.UtcNow;
    }

    public ValueTask<GpuSnapshot> SampleAsync(CancellationToken cancellationToken)
    {
        DateTimeOffset now = _clock.UtcNow;
        if (now - _lastRefreshUtc >= _refreshInterval)
        {
            RefreshWildcards(force: false);
            _lastRefreshUtc = now;
        }

        bool collected = _query.Collect();
        if (!collected)
        {
            if (_recovery.RecordCollectResult(succeeded: false))
            {
                _ = TryRebuildQuery();
            }
            return ValueTask.FromResult(GpuSnapshot.Unavailable());
        }
        _ = _recovery.RecordCollectResult(succeeded: true);
        if (_samplesCollected == 0)
        {
            _samplesCollected = 1;
            return ValueTask.FromResult(GpuSnapshot.Warmup());
        }

        var engineReadings = new List<GpuEngineReading>();
        var memoryReadings = new List<GpuMemoryReading>();
        int expectedEngineValues = 0;
        int expectedMemoryValues = 0;
        int validEngineValues = 0;
        int validMemoryValues = 0;
        foreach (string path in _query.CounterPaths)
        {
            if (!TryExtractInstance(path, out string instance))
            {
                continue;
            }
            bool isEngine = path.Contains("\\GPU Engine(", StringComparison.OrdinalIgnoreCase);
            bool isMemory = path.Contains("\\GPU Adapter Memory(", StringComparison.OrdinalIgnoreCase);
            if (!isEngine && !isMemory)
            {
                continue;
            }
            if (isEngine)
            {
                expectedEngineValues++;
            }
            else
            {
                expectedMemoryValues++;
            }
            if (!_query.TryGetDouble(path, out double value))
            {
                continue;
            }
            if (isEngine)
            {
                if (!GpuInstanceName.TryParse(instance, out var parsed))
                {
                    continue;
                }
                validEngineValues++;
                engineReadings.Add(new GpuEngineReading(
                    parsed.Luid,
                    parsed.PhysicalAdapter,
                    parsed.EngineIndex,
                    parsed.EngineType,
                    value));
            }
            else if (GpuInstanceName.TryParseMemory(instance, out var memory))
            {
                validMemoryValues++;
                memoryReadings.Add(new GpuMemoryReading(memory.Luid, (long)Math.Clamp(value, 0, long.MaxValue)));
            }
        }
        bool rebuildForEngine = _recovery.RecordFormattedValues(expectedEngineValues, validEngineValues);
        bool rebuildForMemory = _memoryRecovery.RecordFormattedValues(expectedMemoryValues, validMemoryValues);
        if (rebuildForEngine || rebuildForMemory)
        {
            _ = TryRebuildQuery();
            return ValueTask.FromResult(GpuSnapshot.Unavailable());
        }
        EnsureDxgiMatches(engineReadings.Select(reading => reading.Luid)
            .Concat(memoryReadings.Select(reading => reading.Luid)));
        return ValueTask.FromResult(GpuMetricAggregator.Aggregate(
            engineReadings,
            memoryReadings,
            _dxgi,
            _preferredAdapter.Value));
    }

    public void SetPreferredAdapter(ulong? luid) => _preferredAdapter.Set(luid);

    public int CounterCount => _query.CounterPaths.Count;

    private void EnsureDxgiMatches(IEnumerable<ulong> observedLuids)
    {
        if (_dxgiOverride.Count > 0)
        {
            return;
        }
        var known = _dxgi.Where(adapter => !adapter.IsSoftware).Select(adapter => adapter.Luid).ToHashSet();
        if (!observedLuids.Any(luid => !known.Contains(luid)))
        {
            return;
        }
        try
        {
            _dxgi = DxgiAdapterEnumerator.Enumerate();
            _lastDxgiEnumUtc = _clock.UtcNow;
        }
        catch
        {
            // Preserve the prior cache and retry on the next mismatch.
        }
    }

    private bool TryRebuildQuery()
    {
        PdhQuery replacement;
        try
        {
            replacement = new PdhQuery();
        }
        catch
        {
            return false;
        }

        PdhQuery previous = _query;
        _query = replacement;
        try
        {
            RefreshWildcards(force: true);
            _samplesCollected = 0;
            _lastRefreshUtc = _clock.UtcNow;
            _recovery.Reset();
            _memoryRecovery.Reset();
            previous.Dispose();
            return true;
        }
        catch
        {
            _query = previous;
            replacement.Dispose();
            return false;
        }
    }

    private void RefreshWildcards(bool force)
    {
        WildcardRefreshCount++;
        // Rebuild the DXGI cache on first use/force and at most once every
        // 30 seconds during periodic wildcard refresh.
        if (_dxgiOverride.Count > 0)
        {
            _dxgi = _dxgiOverride;
        }
        else if (force || _clock.UtcNow - _lastDxgiEnumUtc >= TimeSpan.FromSeconds(30))
        {
            try
            {
                _dxgi = DxgiAdapterEnumerator.Enumerate();
            }
            catch
            {
                _dxgi = Array.Empty<DxgiAdapterInfo>();
            }
            _lastDxgiEnumUtc = _clock.UtcNow;
        }

        var engineInstances = PdhQuery.ExpandWildcard(EngineWildcard);
        var memoryInstances = PdhQuery.ExpandWildcard(MemoryWildcard);

        var desiredPaths = new HashSet<string>(StringComparer.Ordinal);
        foreach (var p in engineInstances)
        {
            desiredPaths.Add(p);
        }
        foreach (var p in memoryInstances)
        {
            desiredPaths.Add(p);
        }

        var current = new HashSet<string>(_query.CounterPaths, StringComparer.Ordinal);
        // Remove obsolete first
        foreach (var path in current)
        {
            if (!desiredPaths.Contains(path))
            {
                _query.RemoveCounter(path);
            }
        }
        int added = 0;
        foreach (var path in desiredPaths)
        {
            if (!current.Contains(path))
            {
                if (_query.TryAddCounter(path))
                {
                    added++;
                }
            }
        }
        if (added > 0)
        {
            // New rate counters need a fresh baseline; force one more warmup tick.
            _samplesCollected = 0;
        }
    }

    private static bool TryExtractInstance(string path, out string instance)
    {
        instance = string.Empty;
        int open = path.IndexOf('(');
        int close = path.IndexOf(')', open + 1);
        if (open < 0 || close < 0 || close <= open + 1)
        {
            return false;
        }
        instance = path[(open + 1)..close];
        return true;
    }

    public void ResetBaseline()
    {
        _samplesCollected = 0;
        _recovery.Reset();
        _memoryRecovery.Reset();
        _ = TryRebuildQuery();
    }

    public void Dispose()
    {
        _query.Dispose();
    }

}

internal sealed class PreferredAdapterSelectionStore
{
    private sealed record Selection(ulong? Value);

    private Selection _selection = new(null);

    internal ulong? Value => Volatile.Read(ref _selection).Value;

    internal void Set(ulong? value) => Volatile.Write(ref _selection, new Selection(value));
}
