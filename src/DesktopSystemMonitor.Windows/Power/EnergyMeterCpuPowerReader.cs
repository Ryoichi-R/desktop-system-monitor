using System.Globalization;
using DesktopSystemMonitor.Core.Metrics;
using DesktopSystemMonitor.Windows.Pdh;

namespace DesktopSystemMonitor.Windows.Power;

internal readonly record struct CpuPowerReading(MetricStatus Status, double Watts)
{
    public static CpuPowerReading Unavailable() => new(MetricStatus.Unavailable, double.NaN);
}

internal interface ICpuPowerReader : IDisposable
{
    CpuPowerReading Sample();
}

/// <summary>
/// Reads Windows Energy Meter Intel RAPL package counters, falling back to
/// Snapdragon-style CPU cluster counters only when no package counter exists.
/// The provider reports power in milliwatts, so values are normalized to watts.
/// </summary>
internal sealed class EnergyMeterCpuPowerReader : ICpuPowerReader
{
    internal const string PowerWildcardPath = @"\Energy Meter(*)\Power";
    internal const double MilliwattsPerWatt = 1_000d;
    internal const double MaximumReasonableMilliwatts =
        PowerSensorSelector.MaximumReasonableWatts * MilliwattsPerWatt;

    private readonly IPowerCounterQuery _query;
    private readonly string[] _paths;
    private readonly CpuPowerCounterKind _counterKind;
    private readonly Action<string, Exception>? _onError;

    private EnergyMeterCpuPowerReader(
        IPowerCounterQuery query,
        IEnumerable<string> expandedPaths,
        Action<string, Exception>? onError)
    {
        _query = query;
        _onError = onError;
        SelectedCpuPowerPaths selected = SelectPowerPaths(expandedPaths);
        _paths = selected.Paths;
        _counterKind = selected.Kind;

        if (_paths.Length == 0
            || _paths.Any(path => !_query.TryAddCounter(path)))
        {
            throw new InvalidOperationException(
                "Compatible Windows Energy Meter CPU power counters are unavailable.");
        }
    }

    public static ICpuPowerReader TryCreate(Action<string, Exception>? onError)
    {
        PdhPowerCounterQuery? query = null;
        try
        {
            query = new PdhPowerCounterQuery();
            return TryCreateOwned(
                query,
                PdhQuery.ExpandWildcard(PowerWildcardPath),
                onError);
        }
        catch (Exception ex)
        {
            query?.Dispose();
            HardwarePowerMetricSource.Report(onError, "cpu-energy-meter-initialization-failure", ex);
            return UnavailableCpuPowerReader.Instance;
        }
    }

    internal static ICpuPowerReader TryCreateForTest(
        IPowerCounterQuery query,
        IEnumerable<string> expandedPaths,
        Action<string, Exception>? onError = null)
        => TryCreateOwned(query, expandedPaths, onError);

    internal static EnergyMeterCpuPowerReader CreateForTest(
        IPowerCounterQuery query,
        IEnumerable<string> expandedPaths,
        Action<string, Exception>? onError = null)
        => new(query, expandedPaths, onError);

    private static ICpuPowerReader TryCreateOwned(
        IPowerCounterQuery query,
        IEnumerable<string> expandedPaths,
        Action<string, Exception>? onError)
    {
        try
        {
            return new EnergyMeterCpuPowerReader(query, expandedPaths, onError);
        }
        catch (Exception ex)
        {
            query.Dispose();
            HardwarePowerMetricSource.Report(onError, "cpu-energy-meter-initialization-failure", ex);
            return UnavailableCpuPowerReader.Instance;
        }
    }

    public CpuPowerReading Sample()
    {
        try
        {
            if (!_query.Collect())
            {
                return CpuPowerReading.Unavailable();
            }

            double totalMilliwatts = 0;
            foreach (string path in _paths)
            {
                if (!_query.TryGetDouble(path, out double milliwatts)
                    || !IsValidIndividualMilliwatts(milliwatts, _counterKind))
                {
                    return CpuPowerReading.Unavailable();
                }
                totalMilliwatts += milliwatts;
            }

            if (!IsValidTotalMilliwatts(totalMilliwatts))
            {
                return CpuPowerReading.Unavailable();
            }
            return new CpuPowerReading(MetricStatus.Ok, totalMilliwatts / MilliwattsPerWatt);
        }
        catch (Exception ex)
        {
            HardwarePowerMetricSource.Report(_onError, "cpu-energy-meter-sample-failure", ex);
            return CpuPowerReading.Unavailable();
        }
    }

    public void Dispose() => _query.Dispose();

    internal static bool IsPackagePowerPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path)
            || !path.EndsWith(@")\Power", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }
        int close = path.Length - @"\Power".Length - 1;
        int open = path.LastIndexOf('(', close);
        return open >= 0
            && close > open
            && path.AsSpan(open + 1, close - open - 1)
                .EndsWith("_PKG", StringComparison.OrdinalIgnoreCase);
    }

    internal static bool IsClusterPowerPath(string path) =>
        TryGetClusterCandidate(path, out _);

    internal static SelectedCpuPowerPaths SelectPowerPaths(IEnumerable<string> expandedPaths)
    {
        ArgumentNullException.ThrowIfNull(expandedPaths);
        string[] allPaths = expandedPaths.ToArray();
        string[] packages = allPaths
            .Where(IsPackagePowerPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (packages.Length > 0)
        {
            return new SelectedCpuPowerPaths(CpuPowerCounterKind.Package, packages);
        }

        string[] clusters = allPaths
            .Select(path => TryGetClusterCandidate(path, out ClusterPathCandidate candidate)
                ? candidate
                : (ClusterPathCandidate?)null)
            .Where(candidate => candidate.HasValue)
            .Select(candidate => candidate!.Value)
            .GroupBy(candidate => candidate.Index)
            .OrderBy(group => group.Key)
            .Select(group => group
                .OrderBy(candidate => candidate.IsCanonical ? 0 : 1)
                .ThenBy(candidate => candidate.Path, StringComparer.OrdinalIgnoreCase)
                .ThenBy(candidate => candidate.Path, StringComparer.Ordinal)
                .First()
                .Path)
            .ToArray();

        return clusters.Length == 0
            ? SelectedCpuPowerPaths.None
            : new SelectedCpuPowerPaths(CpuPowerCounterKind.Cluster, clusters);
    }

    private static bool TryGetClusterCandidate(
        string? path,
        out ClusterPathCandidate candidate)
    {
        candidate = default;
        if (string.IsNullOrWhiteSpace(path)
            || !path.EndsWith(@")\Power", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        int close = path.Length - @"\Power".Length - 1;
        int open = path.LastIndexOf('(', close);
        if (open < 0 || close <= open)
        {
            return false;
        }

        ReadOnlySpan<char> instance = path.AsSpan(open + 1, close - open - 1);
        const string prefix = "CPU_CLUSTER_";
        if (!instance.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        ReadOnlySpan<char> suffix = instance[prefix.Length..];
        if (suffix.IsEmpty
            || !int.TryParse(
                suffix,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out int index))
        {
            return false;
        }

        string canonicalInstance = string.Concat(prefix, index.ToString(CultureInfo.InvariantCulture));
        candidate = new ClusterPathCandidate(
            path,
            index,
            instance.Equals(canonicalInstance, StringComparison.OrdinalIgnoreCase));
        return true;
    }

    private static bool IsValidIndividualMilliwatts(
        double value,
        CpuPowerCounterKind counterKind) =>
        double.IsFinite(value)
        && value <= MaximumReasonableMilliwatts
        && (counterKind == CpuPowerCounterKind.Cluster ? value >= 0 : value > 0);

    private static bool IsValidTotalMilliwatts(double value) =>
        double.IsFinite(value) && value > 0 && value <= MaximumReasonableMilliwatts;

    private readonly record struct ClusterPathCandidate(
        string Path,
        int Index,
        bool IsCanonical);
}

internal enum CpuPowerCounterKind
{
    None,
    Package,
    Cluster,
}

internal readonly record struct SelectedCpuPowerPaths(
    CpuPowerCounterKind Kind,
    string[] Paths)
{
    public static SelectedCpuPowerPaths None { get; } =
        new(CpuPowerCounterKind.None, []);
}

internal interface IPowerCounterQuery : IDisposable
{
    bool TryAddCounter(string path);
    bool Collect();
    bool TryGetDouble(string path, out double value);
}

internal sealed class PdhPowerCounterQuery : IPowerCounterQuery
{
    private readonly PdhQuery _query = new();

    public bool TryAddCounter(string path) => _query.TryAddCounter(path);
    public bool Collect() => _query.Collect();
    public bool TryGetDouble(string path, out double value) => _query.TryGetDouble(path, out value);
    public void Dispose() => _query.Dispose();
}

internal sealed class UnavailableCpuPowerReader : ICpuPowerReader
{
    public static UnavailableCpuPowerReader Instance { get; } = new();

    private UnavailableCpuPowerReader()
    {
    }

    public CpuPowerReading Sample() => CpuPowerReading.Unavailable();
    public void Dispose()
    {
    }
}

/// <summary>
/// Retries failed initialization and replaces a live reader when it remains
/// unavailable for one retry interval. A successful sample cancels a pending
/// replacement so a transient PDH failure does not rebuild the query.
/// </summary>
internal sealed class RetryingCpuPowerReader : ICpuPowerReader
{
    private readonly Func<ICpuPowerReader> _factory;
    private readonly TimeSpan _retryInterval;
    private readonly Func<DateTimeOffset> _utcNow;
    private ICpuPowerReader _inner;
    private DateTimeOffset _nextRetry;
    private bool _retryScheduled;

    public RetryingCpuPowerReader(
        Func<ICpuPowerReader> factory,
        TimeSpan retryInterval,
        Func<DateTimeOffset>? utcNow = null)
    {
        ArgumentNullException.ThrowIfNull(factory);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(retryInterval, TimeSpan.Zero);
        _factory = factory;
        _retryInterval = retryInterval;
        _utcNow = utcNow ?? (() => DateTimeOffset.UtcNow);
        _inner = CreateOrUnavailable();
        ScheduleRetryIfUnavailable();
    }

    public CpuPowerReading Sample()
    {
        DateTimeOffset now = _utcNow();
        if (_retryScheduled && now >= _nextRetry)
        {
            ICpuPowerReader previous = _inner;
            _inner = CreateOrUnavailable();
            _retryScheduled = false;
            if (!ReferenceEquals(previous, _inner))
            {
                previous.Dispose();
            }
        }

        CpuPowerReading reading = _inner.Sample();
        if (reading.Status == MetricStatus.Ok)
        {
            _retryScheduled = false;
        }
        else if (!_retryScheduled)
        {
            _nextRetry = now + _retryInterval;
            _retryScheduled = true;
        }
        return reading;
    }

    public void Dispose() => _inner.Dispose();

    private ICpuPowerReader CreateOrUnavailable()
    {
        try
        {
            return _factory() ?? UnavailableCpuPowerReader.Instance;
        }
        catch
        {
            return UnavailableCpuPowerReader.Instance;
        }
    }

    private void ScheduleRetryIfUnavailable()
    {
        if (ReferenceEquals(_inner, UnavailableCpuPowerReader.Instance))
        {
            _nextRetry = _utcNow() + _retryInterval;
            _retryScheduled = true;
        }
    }
}
