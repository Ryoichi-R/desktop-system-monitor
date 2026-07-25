using DesktopSystemMonitor.Core.Metrics;
using DesktopSystemMonitor.Windows.Pdh;

namespace DesktopSystemMonitor.Windows.Cpu;

/// <summary>
/// Reads CPU utilization from <c>Processor Information(_Total)\% Processor Utility</c>
/// and estimates current frequency from <c>% Processor Performance</c> times
/// the MaxMhz reported by <c>CallNtPowerInformation</c>. Both are considered
/// warming up until the first successful post-open collection.
/// </summary>
public sealed class CpuMetricSource : IMetricSource<CpuSnapshot>
{
    private const string UtilityPath = @"\Processor Information(_Total)\% Processor Utility";
    private const string PerformancePath = @"\Processor Information(_Total)\% Processor Performance";
    private const string TimePath = @"\Processor Information(_Total)\% Processor Time";

    private readonly PdhQuery _query;
    private readonly uint _maxMhz;
    private int _samplesCollected;

    public CpuMetricSource()
    {
        _query = new PdhQuery();
        _query.TryAddCounter(UtilityPath);
        _query.TryAddCounter(PerformancePath);
        _query.TryAddCounter(TimePath);
        _maxMhz = ProcessorPowerInformation.GetMaxMhz();
    }

    public ValueTask<CpuSnapshot> SampleAsync(CancellationToken cancellationToken)
    {
        if (!_query.Collect())
        {
            return ValueTask.FromResult(CpuSnapshot.Unavailable());
        }
        // First successful collect only establishes the baseline for rate counters.
        if (_samplesCollected == 0)
        {
            _samplesCollected = 1;
            return ValueTask.FromResult(CpuSnapshot.Warmup());
        }

        // Prefer % Processor Utility; if unavailable fall back to % Processor Time.
        MetricStatus utilStatus = MetricStatus.Ok;
        double raw;
        if (!_query.TryGetDouble(UtilityPath, out raw))
        {
            if (!_query.TryGetDouble(TimePath, out raw))
            {
                utilStatus = MetricStatus.Unavailable;
                raw = double.NaN;
            }
        }
        double clampedUtil = double.IsNaN(raw) ? double.NaN : Math.Clamp(raw, 0d, 100d);

        MetricStatus freqStatus = MetricStatus.Ok;
        double freqMhz;
        if (_maxMhz == 0)
        {
            freqStatus = MetricStatus.Unavailable;
            freqMhz = double.NaN;
        }
        else if (_query.TryGetDouble(PerformancePath, out double perf))
        {
            freqMhz = _maxMhz * perf / 100d;
            if (double.IsNaN(freqMhz) || freqMhz < 0)
            {
                freqStatus = MetricStatus.Unavailable;
                freqMhz = double.NaN;
            }
        }
        else
        {
            freqStatus = MetricStatus.Unavailable;
            freqMhz = double.NaN;
        }

        return ValueTask.FromResult(new CpuSnapshot
        {
            UtilizationStatus = utilStatus,
            UtilizationPercent = clampedUtil,
            FrequencyStatus = freqStatus,
            FrequencyMhz = freqMhz,
            FrequencyIsEstimate = true,
            RawUtilizationPercent = double.IsNaN(raw) ? null : raw,
        });
    }

    public void ResetBaseline()
    {
        _samplesCollected = 0;
    }

    public void Dispose()
    {
        _query.Dispose();
    }
}
