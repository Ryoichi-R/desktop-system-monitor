using System.Diagnostics;

namespace DesktopSystemMonitor.Windows.Processes;

public enum ProcessLoadSort
{
    Cpu,
    Memory,
    Io,
}

public enum ProcessLoadDiagnosticEvent
{
    PartialReadFailure,
    BudgetExceeded,
    IntervalRecovered,
}

public interface IProcessLoadDiagnostics
{
    void Record(ProcessLoadDiagnosticEvent eventCode, int processCount, TimeSpan elapsed);
}

public sealed record ProcessLoadRow(string Name, int ProcessId, double CpuPercent, long? PrivateWorkingSetBytes, double ReadBytesPerSecond, double WriteBytesPerSecond);

public sealed class ProcessLoadSampler : IDisposable
{
    private readonly Dictionary<(int Pid, DateTime Start), Baseline> _baselines = [];
    private readonly Func<Process[]> _getProcesses;
    private readonly TimeProvider _timeProvider;
    private readonly int _processorCount;
    private readonly IProcessLoadDiagnostics _diagnostics;
    private readonly IPrivateWorkingSetReader _privateWorkingSetReader;
    private DateTimeOffset? _lastSample;
    private int _fastCycles;

    public ProcessLoadSampler() : this(
        Process.GetProcesses,
        TimeProvider.System,
        Environment.ProcessorCount,
        NullProcessLoadDiagnostics.Instance)
    {
    }

    internal ProcessLoadSampler(
        Func<Process[]> getProcesses,
        TimeProvider timeProvider,
        int processorCount,
        IProcessLoadDiagnostics diagnostics,
        IPrivateWorkingSetReader? privateWorkingSetReader = null)
    {
        _getProcesses = getProcesses;
        _timeProvider = timeProvider;
        _processorCount = Math.Max(1, processorCount);
        _diagnostics = diagnostics;
        _privateWorkingSetReader = privateWorkingSetReader ?? new PrivateWorkingSetReader();
    }

    public TimeSpan NextInterval { get; private set; } = TimeSpan.FromSeconds(2);

    public IReadOnlyList<ProcessLoadRow> Sample(
        ProcessLoadSort sort = ProcessLoadSort.Cpu,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        long startedAt = _timeProvider.GetTimestamp();
        DateTimeOffset now = _timeProvider.GetUtcNow();
        double elapsed = _lastSample is null ? 0 : (now - _lastSample.Value).TotalSeconds;
        _lastSample = now;
        var rows = new List<ProcessLoadRow>();
        var seen = new HashSet<(int, DateTime)>();
        int failedProcesses = 0;
        Process[] processes = _getProcesses();
        _privateWorkingSetReader.BeginSample();
        try
        {
            foreach (Process process in processes)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    DateTime start = process.StartTime.ToUniversalTime();
                    var key = (process.Id, start);
                    seen.Add(key);
                    TimeSpan cpu = process.TotalProcessorTime;
                    long? memory = _privateWorkingSetReader.TryGetBytes(process, out long privateWorkingSet)
                        ? privateWorkingSet
                        : null;
                    double read = double.NaN;
                    double write = double.NaN;
                    ulong? readTotal = null;
                    ulong? writeTotal = null;
                    if (ProcessIoInterop.GetProcessIoCounters(process.SafeHandle, out var io))
                    {
                        readTotal = io.ReadTransferCount;
                        writeTotal = io.WriteTransferCount;
                    }
                    double cpuPercent = double.NaN;
                    if (elapsed > 0 && _baselines.TryGetValue(key, out Baseline previous))
                    {
                        cpuPercent = Math.Clamp((cpu - previous.Cpu).TotalSeconds / (elapsed * _processorCount) * 100d, 0d, 100d);
                        if (readTotal is { } currentRead && previous.Read is { } priorRead)
                        {
                            read = currentRead >= priorRead ? (currentRead - priorRead) / elapsed : double.NaN;
                        }
                        if (writeTotal is { } currentWrite && previous.Write is { } priorWrite)
                        {
                            write = currentWrite >= priorWrite ? (currentWrite - priorWrite) / elapsed : double.NaN;
                        }
                    }
                    _baselines[key] = new(cpu, readTotal, writeTotal);
                    rows.Add(new ProcessLoadRow(process.ProcessName, process.Id, cpuPercent, memory, read, write));
                }
                catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException)
                {
                    // Processes can exit or deny access between enumeration and reading.
                    failedProcesses++;
                }
            }
        }
        finally
        {
            foreach (Process process in processes)
            {
                process.Dispose();
            }
        }
        foreach (var stale in _baselines.Keys.Where(key => !seen.Contains(key)).ToArray())
        {
            _baselines.Remove(stale);
        }
        TimeSpan executionTime = _timeProvider.GetElapsedTime(startedAt);
        if (failedProcesses > 0)
        {
            _diagnostics.Record(ProcessLoadDiagnosticEvent.PartialReadFailure, failedProcesses, executionTime);
        }
        if (executionTime > TimeSpan.FromMilliseconds(250))
        {
            NextInterval = TimeSpan.FromSeconds(5);
            _fastCycles = 0;
            _diagnostics.Record(ProcessLoadDiagnosticEvent.BudgetExceeded, processes.Length, executionTime);
        }
        else if (NextInterval == TimeSpan.FromSeconds(5) && ++_fastCycles >= 3)
        {
            NextInterval = TimeSpan.FromSeconds(2);
            _fastCycles = 0;
            _diagnostics.Record(ProcessLoadDiagnosticEvent.IntervalRecovered, processes.Length, executionTime);
        }
        IEnumerable<ProcessLoadRow> ordered = sort switch
        {
            ProcessLoadSort.Memory => rows
                .OrderByDescending(row => row.PrivateWorkingSetBytes.HasValue)
                .ThenByDescending(row => row.PrivateWorkingSetBytes),
            ProcessLoadSort.Io => rows.OrderByDescending(row => Safe(row.ReadBytesPerSecond) + Safe(row.WriteBytesPerSecond)),
            _ => rows.OrderByDescending(row => Safe(row.CpuPercent)),
        };
        return ordered.Take(10).ToArray();
    }

    public void Reset()
    {
        _baselines.Clear();
        _lastSample = null;
        _fastCycles = 0;
        NextInterval = TimeSpan.FromSeconds(2);
    }

    private static double Safe(double value) => double.IsFinite(value) ? value : -1d;
    private readonly record struct Baseline(TimeSpan Cpu, ulong? Read, ulong? Write);

    private sealed class NullProcessLoadDiagnostics : IProcessLoadDiagnostics
    {
        public static NullProcessLoadDiagnostics Instance { get; } = new();
        public void Record(ProcessLoadDiagnosticEvent eventCode, int processCount, TimeSpan elapsed) { }
    }

    public void Dispose()
    {
        _privateWorkingSetReader.Dispose();
        GC.SuppressFinalize(this);
    }
}
