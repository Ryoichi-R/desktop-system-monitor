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

    public bool TryAddCounter(string englishPath)
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
            hr = PdhInterop.PdhAddEnglishCounter(_query, englishPath, IntPtr.Zero, out counter);
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
    public static IReadOnlyList<string> ExpandWildcard(string wildcardPath)
    {
        ArgumentNullException.ThrowIfNull(wildcardPath);
        uint size = 0;
        _ = PdhInterop.PdhExpandWildCardPath(IntPtr.Zero, wildcardPath, IntPtr.Zero, ref size, PdhInterop.PDH_REFRESHCOUNTERS);
        if (size == 0)
        {
            return Array.Empty<string>();
        }
        // Allocate wchar buffer
        IntPtr buffer = Marshal.AllocHGlobal(checked((int)(size * sizeof(char))));
        try
        {
            uint hr = PdhInterop.PdhExpandWildCardPath(IntPtr.Zero, wildcardPath, buffer, ref size, PdhInterop.PDH_REFRESHCOUNTERS);
            if (hr != 0)
            {
                return Array.Empty<string>();
            }
            return ReadMultiSz(buffer, (int)size);
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
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
