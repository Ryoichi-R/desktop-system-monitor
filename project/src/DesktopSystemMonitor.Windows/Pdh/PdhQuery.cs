using System.Runtime.InteropServices;

namespace DesktopSystemMonitor.Windows.Pdh;

/// <summary>
/// RAII wrapper around a PDH query handle plus its counter handles. Read
/// results with <see cref="TryGetDouble"/>: it inspects the counter's CStatus
/// and rejects any non-valid/new sample rather than surfacing invalid data.
/// </summary>
public sealed class PdhQuery : IDisposable
{
    private readonly SafePdhQueryHandle _query;
    private readonly Dictionary<string, IntPtr> _counters = new(StringComparer.Ordinal);
    private readonly List<string> _order = new();
    private bool _hasCollected;

    public PdhQuery()
    {
        uint hr = PdhInterop.PdhOpenQuery(IntPtr.Zero, IntPtr.Zero, out IntPtr query);
        var handle = new SafePdhQueryHandle(query);
        if (hr != 0 || query == IntPtr.Zero)
        {
            handle.Dispose();
            throw new InvalidOperationException($"PdhOpenQuery failed 0x{hr:X8}");
        }
        _query = handle;
    }

    public bool TryAddCounter(string englishPath) => TryAddCounter(englishPath, null);

    public bool TryAddCounter(ExpandedCounter counter) => TryAddCounter(counter.EnglishPath, counter.LocalizedPath);

    private bool TryAddCounter(string englishPath, string? localizedPath)
    {
        ArgumentNullException.ThrowIfNull(englishPath);
        if (_query.IsClosed || _query.IsInvalid)
        {
            return false;
        }
        if (_counters.ContainsKey(englishPath))
        {
            return true;
        }
        uint hr;
        IntPtr counter;
        try
        {
            hr = localizedPath is null
                ? PdhInterop.PdhAddEnglishCounter(_query, englishPath, IntPtr.Zero, out counter)
                : PdhInterop.AddLocalizedCounter(_query, localizedPath, IntPtr.Zero, out counter);
        }
        catch (ObjectDisposedException)
        {
            return false;
        }
        if (hr != 0 || counter == IntPtr.Zero)
        {
            return false;
        }
        _counters[englishPath] = counter;
        _order.Add(englishPath);
        return true;
    }

    public bool RemoveCounter(string englishPath)
    {
        if (_query.IsClosed || _query.IsInvalid)
        {
            return false;
        }
        if (!_counters.TryGetValue(englishPath, out IntPtr counter))
        {
            return false;
        }
        _ = PdhInterop.PdhRemoveCounter(counter);
        _counters.Remove(englishPath);
        _order.Remove(englishPath);
        return true;
    }

    public IReadOnlyList<string> CounterPaths => _order;

    public bool Collect()
    {
        if (_query.IsClosed || _query.IsInvalid)
        {
            return false;
        }
        uint hr;
        try
        {
            hr = PdhInterop.PdhCollectQueryData(_query);
        }
        catch (ObjectDisposedException)
        {
            return false;
        }
        if (hr == 0)
        {
            _hasCollected = true;
            return true;
        }
        return false;
    }

    public bool TryGetDouble(string englishPath, out double value)
    {
        value = double.NaN;
        if (_query.IsClosed || _query.IsInvalid || !_hasCollected)
        {
            return false;
        }
        if (!_counters.TryGetValue(englishPath, out IntPtr counter))
        {
            return false;
        }
        bool handleAdded = false;
        uint hr;
        PdhInterop.PDH_FMT_COUNTERVALUE result;
        try
        {
            _query.DangerousAddRef(ref handleAdded);
            if (_query.IsInvalid || _query.IsClosed)
            {
                return false;
            }
            hr = PdhInterop.PdhGetFormattedCounterValue(
                counter,
                PdhInterop.PDH_FMT_DOUBLE | PdhInterop.PDH_FMT_NOCAP100,
                out _,
                out result);
        }
        catch (ObjectDisposedException)
        {
            return false;
        }
        finally
        {
            if (handleAdded)
            {
                _query.DangerousRelease();
            }
        }
        if (hr != 0)
        {
            return false;
        }
        if (result.CStatus != PdhInterop.PDH_CSTATUS_VALID_DATA
            && result.CStatus != PdhInterop.PDH_CSTATUS_NEW_DATA)
        {
            return false;
        }
        double v = result.doubleValue;
        if (double.IsNaN(v) || double.IsInfinity(v))
        {
            return false;
        }
        value = v;
        return true;
    }

    /// <summary>
    /// Expand a wildcard counter path via <c>PdhExpandWildCardPathW</c>. Uses
    /// the English name registry via a temporary query, so callers should pass
    /// the English form.
    /// </summary>
    public static IReadOnlyList<string> ExpandWildcard(string wildcardPath) =>
        ExpandCounters(wildcardPath).Select(counter => counter.EnglishPath).ToArray();

    public static IReadOnlyList<ExpandedCounter> ExpandCounters(string wildcardPath)
    {
        ArgumentNullException.ThrowIfNull(wildcardPath);
        using var temporary = new PdhQuery();
        if (!temporary.TryAddCounter(wildcardPath)) return [];
        IntPtr handle = temporary._counters[wildcardPath];
        uint size = 0;
        if (PdhInterop.GetCounterInfo(handle, 0, ref size, IntPtr.Zero) != PdhInterop.PDH_MORE_DATA
            || size < Marshal.SizeOf<PdhInterop.CounterInfoHeader>()) return [];
        IntPtr buffer = Marshal.AllocHGlobal(checked((int)size));
        try
        {
            if (PdhInterop.GetCounterInfo(handle, 0, ref size, buffer) != 0) return [];
            string? localized = Marshal.PtrToStringUni(Marshal.PtrToStructure<PdhInterop.CounterInfoHeader>(buffer).FullPath);
            return localized is null ? [] : ExpandInstances(wildcardPath, localized, PdhInterop.PdhExpandWildCardPath);
        }
        finally { Marshal.FreeHGlobal(buffer); }
    }

    internal delegate uint ExpandPath(IntPtr source, string path, IntPtr buffer, ref uint size, uint flags);

    internal static IReadOnlyList<ExpandedCounter> ExpandInstances(string english, string localized, ExpandPath expand)
    {
        // The providers enumerate instances of a single named counter.
        int star = localized.IndexOf("(*)", StringComparison.Ordinal);
        if (star < 0 || !english.Contains("(*)", StringComparison.Ordinal)) return [];
        int objectStart = localized.LastIndexOf('\\', star);
        if (objectStart < 0) return [];
        string prefix = localized[objectStart..(star + 1)];
        string suffix = localized[(star + 2)..];
        for (int attempt = 0; attempt < 3; attempt++)
        {
            uint size = 0;
            uint status = expand(IntPtr.Zero, localized, IntPtr.Zero, ref size, PdhInterop.PDH_REFRESHCOUNTERS);
            if (status != PdhInterop.PDH_MORE_DATA || size == 0) break;
            uint capacity = size;
            IntPtr buffer = Marshal.AllocHGlobal(checked((int)size * sizeof(char)));
            try
            {
                status = expand(IntPtr.Zero, localized, buffer, ref size, PdhInterop.PDH_REFRESHCOUNTERS);
                if (status == PdhInterop.PDH_MORE_DATA) continue;
                if (status != 0 || size > capacity) break;
                var results = new List<ExpandedCounter>();
                foreach (string path in ReadMultiSz(buffer, (int)size))
                {
                    int start = path.IndexOf(prefix, StringComparison.OrdinalIgnoreCase);
                    if (start < 0 || !path.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)) continue;
                    start += prefix.Length;
                    int end = path.Length - suffix.Length;
                    if (end <= start) continue;
                    string instance = path[start..end];
                    results.Add(new(english.Replace("(*)", $"({instance})", StringComparison.Ordinal), path, instance));
                }
                return results;
            }
            finally { Marshal.FreeHGlobal(buffer); }
        }
        return [];
    }

    private static List<string> ReadMultiSz(IntPtr buffer, int totalChars)
    {
        var results = new List<string>();
        unsafe
        {
            char* p = (char*)buffer;
            int i = 0;
            while (i < totalChars)
            {
                int start = i;
                while (i < totalChars && p[i] != '\0')
                {
                    i++;
                }
                int len = i - start;
                if (len == 0)
                {
                    break;
                }
                results.Add(new string(p + start, 0, len));
                i++; // skip null
            }
        }
        return results;
    }

    public void Dispose()
    {
        _counters.Clear();
        _order.Clear();
        _query.Dispose();
        GC.SuppressFinalize(this);
    }
}

public readonly record struct ExpandedCounter(string EnglishPath, string LocalizedPath, string Instance);
