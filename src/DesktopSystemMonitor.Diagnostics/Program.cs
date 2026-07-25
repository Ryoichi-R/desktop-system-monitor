using System.Diagnostics;
using System.Globalization;
using DesktopSystemMonitor.Core.Metrics;
using DesktopSystemMonitor.Windows.Cpu;
using DesktopSystemMonitor.Windows.Gpu;
using DesktopSystemMonitor.Windows.Network;
using DesktopSystemMonitor.Windows.Pdh;

namespace DesktopSystemMonitor.Diagnostics;

/// <summary>
/// Phase 0 collector. Captures competing CPU sources in one PDH query, GPU
/// metrics at a selectable wildcard refresh interval, network rates, and the
/// collector's own resource cost for comparison with Task Manager.
/// </summary>
internal static class Program
{
    private static readonly double[] AllowedGpuRefreshSeconds = [1, 2, 5, 10];

    private static async Task<int> Main(string[] args)
    {
        int seconds = 120;
        double gpuRefreshSeconds = 5;
        string outputPath = "diagnostics.csv";
        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--seconds" when i + 1 < args.Length && int.TryParse(args[++i], out int parsedSeconds) && parsedSeconds > 0:
                    seconds = parsedSeconds;
                    break;
                case "--gpu-refresh-seconds" when i + 1 < args.Length &&
                    double.TryParse(args[++i], NumberStyles.Float, CultureInfo.InvariantCulture, out double parsedRefresh) &&
                    AllowedGpuRefreshSeconds.Contains(parsedRefresh):
                    gpuRefreshSeconds = parsedRefresh;
                    break;
                case "--output" when i + 1 < args.Length:
                    outputPath = args[++i];
                    break;
                case "--help":
                    PrintUsage();
                    return 0;
                default:
                    Console.Error.WriteLine($"Invalid argument: {args[i]}");
                    PrintUsage();
                    return 2;
            }
        }

        Console.WriteLine($"Sampling for {seconds} seconds (GPU refresh {gpuRefreshSeconds}s) → {outputPath}");
        Console.WriteLine("Press Ctrl+C to stop early.");

        using var cpu = new CpuCandidateCollector();
        using var gpu = new GpuMetricSource(TimeSpan.FromSeconds(gpuRefreshSeconds), Array.Empty<DxgiAdapterInfo>());
        using var network = new NetworkMetricSource();
        using Process process = Process.GetCurrentProcess();

        await using var writer = new StreamWriter(outputPath);
        writer.WriteLine("timestamp,cpu_utility,cpu_time,cpu_performance,cpu_frequency_mhz,cpu_power_estimate_mhz,cpu_current_mhz,cpu_hyperv_frequency_mhz,gpu_refresh_seconds,gpu_counter_count,gpu_luid,gpu_util,gpu_engine,gpu_used,gpu_limit,net_rx_bps,net_tx_bps,collector_cpu_percent,working_set_bytes,handle_count");

        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            cts.Cancel();
        };

        DateTime end = DateTime.UtcNow.AddSeconds(seconds);
        TimeSpan previousCpu = process.TotalProcessorTime;
        long previousTimestamp = Stopwatch.GetTimestamp();
        bool firstResourceSample = true;
        while (DateTime.UtcNow < end && !cts.IsCancellationRequested)
        {
            CpuCandidateSnapshot cpuSnap = cpu.Sample();
            GpuSnapshot gpuSnap = await gpu.SampleAsync(cts.Token).ConfigureAwait(false);
            NetworkSnapshot netSnap = await network.SampleAsync(cts.Token).ConfigureAwait(false);
            process.Refresh();

            long nowTimestamp = Stopwatch.GetTimestamp();
            TimeSpan nowCpu = process.TotalProcessorTime;
            double elapsedSeconds = Stopwatch.GetElapsedTime(previousTimestamp, nowTimestamp).TotalSeconds;
            double collectorCpu = firstResourceSample || elapsedSeconds <= 0
                ? double.NaN
                : (nowCpu - previousCpu).TotalSeconds / elapsedSeconds / Environment.ProcessorCount * 100;
            firstResourceSample = false;
            previousTimestamp = nowTimestamp;
            previousCpu = nowCpu;

            string line = FormatRow(
                cpuSnap,
                gpuSnap,
                netSnap,
                gpuRefreshSeconds,
                gpu.CounterCount,
                collectorCpu,
                process.WorkingSet64,
                process.HandleCount);
            writer.WriteLine(line);
            Console.WriteLine(line);

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(1), cts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
        Console.WriteLine("Done.");
        return 0;
    }

    private static string FormatRow(
        CpuCandidateSnapshot cpu,
        GpuSnapshot gpu,
        NetworkSnapshot net,
        double gpuRefreshSeconds,
        int gpuCounterCount,
        double collectorCpu,
        long workingSet,
        int handleCount)
    {
        GpuAdapterSnapshot? adapter = gpu.DisplayAdapter;
        string luid = adapter is null ? "" : adapter.Luid.ToString("X", CultureInfo.InvariantCulture);
        string gpuUtil = adapter?.UtilizationStatus == MetricStatus.Ok ? FormatDouble(adapter.UtilizationPercent) : "";
        string gpuEng = adapter?.UtilizationStatus == MetricStatus.Ok ? adapter.BusiestEngineType : "";
        string used = adapter?.MemoryStatus == MetricStatus.Ok ? adapter.DedicatedUsageBytes.ToString(CultureInfo.InvariantCulture) : "";
        string limit = adapter?.MemoryStatus == MetricStatus.Ok ? adapter.DedicatedLimitBytes.ToString(CultureInfo.InvariantCulture) : "";
        string rx = net.AggregateStatus == MetricStatus.Ok ? FormatDouble(net.AggregateBytesReceivedPerSecond) : "";
        string tx = net.AggregateStatus == MetricStatus.Ok ? FormatDouble(net.AggregateBytesSentPerSecond) : "";
        return string.Join(',',
            DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture),
            FormatDouble(cpu.UtilityPercent),
            FormatDouble(cpu.ProcessorTimePercent),
            FormatDouble(cpu.PerformancePercent),
            FormatDouble(cpu.FrequencyMhz),
            FormatDouble(cpu.PowerEstimateMhz),
            FormatDouble(cpu.CurrentMhz),
            FormatDouble(cpu.HyperVFrequencyMhz),
            gpuRefreshSeconds.ToString(CultureInfo.InvariantCulture),
            gpuCounterCount.ToString(CultureInfo.InvariantCulture),
            luid,
            gpuUtil,
            gpuEng,
            used,
            limit,
            rx,
            tx,
            FormatDouble(collectorCpu),
            workingSet.ToString(CultureInfo.InvariantCulture),
            handleCount.ToString(CultureInfo.InvariantCulture));
    }

    private static string FormatDouble(double value) =>
        double.IsFinite(value) ? value.ToString("G6", CultureInfo.InvariantCulture) : "";

    private static void PrintUsage()
    {
        Console.WriteLine("Usage: DesktopSystemMonitor.Diagnostics [--seconds N] [--gpu-refresh-seconds 1|2|5|10] [--output path.csv]");
    }

    private sealed class CpuCandidateCollector : IDisposable
    {
        private const string UtilityPath = @"\Processor Information(_Total)\% Processor Utility";
        private const string TimePath = @"\Processor Information(_Total)\% Processor Time";
        private const string PerformancePath = @"\Processor Information(_Total)\% Processor Performance";
        private const string FrequencyPath = @"\Processor Information(_Total)\Processor Frequency";
        private const string HyperVWildcard = @"\Hyper-V Hypervisor Logical Processor(*)\Frequency";
        private readonly PdhQuery _query = new();
        private readonly uint _maxMhz;
        private bool _hasBaseline;

        public CpuCandidateCollector()
        {
            _query.TryAddCounter(UtilityPath);
            _query.TryAddCounter(TimePath);
            _query.TryAddCounter(PerformancePath);
            _query.TryAddCounter(FrequencyPath);
            foreach (string path in PdhQuery.ExpandWildcard(HyperVWildcard))
            {
                _query.TryAddCounter(path);
            }
            _maxMhz = ProcessorPowerInformation.GetMaxMhz();
        }

        public CpuCandidateSnapshot Sample()
        {
            if (!_query.Collect())
            {
                return CpuCandidateSnapshot.Empty;
            }
            if (!_hasBaseline)
            {
                _hasBaseline = true;
                return CpuCandidateSnapshot.Empty;
            }
            double utility = Read(UtilityPath);
            double time = Read(TimePath);
            double performance = Read(PerformancePath);
            double frequency = Read(FrequencyPath);
            double estimate = _maxMhz > 0 && double.IsFinite(performance)
                ? _maxMhz * performance / 100d
                : double.NaN;
            double current = ProcessorPowerInformation.GetCurrentMhz();
            double hyperV = AverageHyperVFrequency();
            return new CpuCandidateSnapshot(utility, time, performance, frequency, estimate, current, hyperV);
        }

        private double Read(string path) => _query.TryGetDouble(path, out double value) ? value : double.NaN;

        private double AverageHyperVFrequency()
        {
            double total = 0;
            int count = 0;
            foreach (string path in _query.CounterPaths.Where(path => path.Contains("Hyper-V Hypervisor Logical Processor(", StringComparison.OrdinalIgnoreCase)))
            {
                if (_query.TryGetDouble(path, out double value))
                {
                    total += value;
                    count++;
                }
            }
            return count == 0 ? double.NaN : total / count;
        }

        public void Dispose() => _query.Dispose();
    }

    private readonly record struct CpuCandidateSnapshot(
        double UtilityPercent,
        double ProcessorTimePercent,
        double PerformancePercent,
        double FrequencyMhz,
        double PowerEstimateMhz,
        double CurrentMhz,
        double HyperVFrequencyMhz)
    {
        public static CpuCandidateSnapshot Empty { get; } = new(
            double.NaN, double.NaN, double.NaN, double.NaN, double.NaN, double.NaN, double.NaN);
    }
}
