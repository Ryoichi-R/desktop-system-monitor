using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using DesktopSystemMonitor.Windows.Pdh;
using Microsoft.Win32.SafeHandles;

namespace DesktopSystemMonitor.Windows.Processes;

internal interface IPrivateWorkingSetReader : IDisposable
{
    void BeginSample();
    bool TryGetBytes(Process process, out long bytes);
}

internal sealed class PrivateWorkingSetReader : IPrivateWorkingSetReader
{
    private readonly IPrivateWorkingSetReader _inner;

    internal PrivateWorkingSetReader()
    {
        _inner = ProcessMemoryInterop.IsEx2Available()
            ? new ProcessMemoryEx2PrivateWorkingSetReader()
            : new ProcessV2PrivateWorkingSetReader();
    }

    internal PrivateWorkingSetReader(IPrivateWorkingSetReader inner)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
    }

    internal string ProviderName => _inner switch
    {
        ProcessMemoryEx2PrivateWorkingSetReader => "PROCESS_MEMORY_COUNTERS_EX2",
        ProcessV2PrivateWorkingSetReader => "Process V2",
        _ => _inner.GetType().Name,
    };

    public void BeginSample() => _inner.BeginSample();

    public bool TryGetBytes(Process process, out long bytes) =>
        _inner.TryGetBytes(process, out bytes);

    public void Dispose()
    {
        _inner.Dispose();
        GC.SuppressFinalize(this);
    }
}

internal sealed class ProcessMemoryEx2PrivateWorkingSetReader : IPrivateWorkingSetReader
{
    public void BeginSample()
    {
    }

    public bool TryGetBytes(Process process, out long bytes) =>
        ProcessMemoryInterop.TryGetPrivateWorkingSetBytes(process.SafeHandle, out bytes);

    public void Dispose()
    {
        GC.SuppressFinalize(this);
    }
}

internal sealed class ProcessV2PrivateWorkingSetReader : IPrivateWorkingSetReader
{
    internal const string WorkingSetPrivateWildcard = @"\Process V2(*)\Working Set - Private";
    private Dictionary<int, long> _snapshot = new();

    public void BeginSample()
    {
        _snapshot = Capture();
    }

    public bool TryGetBytes(Process process, out long bytes) =>
        _snapshot.TryGetValue(process.Id, out bytes);

    internal static bool TryParseProcessId(string counterPath, out int processId)
    {
        processId = 0;
        const string counterPrefix = @"\Process V2(";
        const string counterSuffix = @")\Working Set - Private";
        if (!counterPath.StartsWith(counterPrefix, StringComparison.OrdinalIgnoreCase)
            || !counterPath.EndsWith(counterSuffix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return TryParseInstanceId(counterPath.AsSpan(counterPrefix.Length,
            counterPath.Length - counterPrefix.Length - counterSuffix.Length), out processId);
    }

    internal static bool TryParseInstanceId(ReadOnlySpan<char> instance, out int processId)
    {
        int separator = instance.LastIndexOf(':');
        processId = 0;
        if (separator < 0) return false;
        return int.TryParse(
            instance[(separator + 1)..],
            NumberStyles.None,
            CultureInfo.InvariantCulture,
            out processId)
            && processId > 0;
    }

    private static Dictionary<int, long> Capture()
    {
        try
        {
            var paths = PdhQuery.ExpandCounters(WorkingSetPrivateWildcard);
            if (paths.Count == 0)
            {
                return new Dictionary<int, long>();
            }

            using var query = new PdhQuery();
            var registered = paths.Where(query.TryAddCounter).DistinctBy(counter => counter.EnglishPath).ToArray();
            if (query.CounterPaths.Count == 0 || !query.Collect())
            {
                return new Dictionary<int, long>();
            }

            var values = new Dictionary<int, long>();
            var duplicates = new HashSet<int>();
            foreach (var counter in registered)
            {
                if (!TryParseInstanceId(counter.Instance, out int processId)
                    || !query.TryGetDouble(counter.EnglishPath, out double value)
                    || value < 0
                    || value > long.MaxValue)
                {
                    continue;
                }

                long bytes = checked((long)value);
                if (!values.TryAdd(processId, bytes))
                {
                    duplicates.Add(processId);
                }
            }
            foreach (int processId in duplicates)
            {
                values.Remove(processId);
            }
            return values;
        }
        catch (Exception ex) when (ex is InvalidOperationException or ExternalException)
        {
            return new Dictionary<int, long>();
        }
    }

    public void Dispose()
    {
        _snapshot = new Dictionary<int, long>();
        GC.SuppressFinalize(this);
    }
}

internal static partial class ProcessMemoryInterop
{
    [StructLayout(LayoutKind.Sequential)]
    internal struct ProcessMemoryCountersEx2
    {
        internal uint Size;
        internal uint PageFaultCount;
        internal nuint PeakWorkingSetSize;
        internal nuint WorkingSetSize;
        internal nuint QuotaPeakPagedPoolUsage;
        internal nuint QuotaPagedPoolUsage;
        internal nuint QuotaPeakNonPagedPoolUsage;
        internal nuint QuotaNonPagedPoolUsage;
        internal nuint PagefileUsage;
        internal nuint PeakPagefileUsage;
        internal nuint PrivateUsage;
        internal nuint PrivateWorkingSetSize;
        internal nuint SharedCommitUsage;
    }

    internal static bool IsEx2Available()
    {
        using Process process = Process.GetCurrentProcess();
        return TryGetPrivateWorkingSetBytes(process.SafeHandle, out _);
    }

    internal static bool TryGetPrivateWorkingSetBytes(SafeProcessHandle process, out long bytes)
    {
        bytes = 0;
        var counters = new ProcessMemoryCountersEx2
        {
            Size = checked((uint)Marshal.SizeOf<ProcessMemoryCountersEx2>()),
        };
        if (!GetProcessMemoryInfo(process, ref counters, counters.Size))
        {
            return false;
        }

        ulong privateWorkingSetSize = (ulong)counters.PrivateWorkingSetSize;
        if (privateWorkingSetSize > long.MaxValue)
        {
            return false;
        }

        bytes = checked((long)privateWorkingSetSize);
        return true;
    }

    [LibraryImport("psapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetProcessMemoryInfo(
        SafeProcessHandle process,
        ref ProcessMemoryCountersEx2 counters,
        uint size);
}
