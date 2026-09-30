using System.Collections.Immutable;
using DesktopSystemMonitor.Core.Metrics;
using DesktopSystemMonitor.Core.Sampling;
using DesktopSystemMonitor.Core.Utility;

namespace DesktopSystemMonitor.Windows.Network;

/// <summary>
/// Polls MIB_IF_TABLE2 and computes per-adapter rx/tx byte rates using
/// <see cref="RateCalculator"/>, so counter reset or wrap on a Wi-Fi/VPN switch
/// never produces a negative or spike value.
///
/// Adapter filtering: when the user has picked specific LUIDs, only those are
/// aggregated (the interface flags are not consulted). Otherwise (auto mode)
/// non-loopback interfaces with OperStatus Up are summed as follows, decided on
/// every sample:
/// <list type="bullet">
/// <item>Hardware mode: if at least one Up row is a hardware interface that is
/// not an NDIS filter (HardwareInterface set, FilterInterface clear), only such
/// rows are summed. This avoids counting the same traffic again on the NDIS
/// filter / intermediate-driver rows and on virtual or tunnel rows.</item>
/// <item>Fallback mode: otherwise every row without FilterInterface is summed.
/// Several non-filter rows that carry the same traffic cannot be told apart
/// here, so double counting can remain.</item>
/// </list>
/// A row with both HardwareInterface and FilterInterface is treated as a filter
/// row. Every row keeps its own rate state regardless of selection, so a row that
/// stays in the table is not re-initialised when the selection switches.
/// </summary>
public sealed class NetworkMetricSource : INetworkMetricSource
{
    private readonly IClock _clock;
    private readonly Func<IReadOnlyList<IpHelperInterop.MIB_IF_ROW2>> _reader;
    private readonly Dictionary<ulong, PerAdapterState> _state = new();
    private ImmutableHashSet<ulong> _selectedLuids;

    public NetworkMetricSource() : this(new SystemClock(), IpHelperInterop.ReadAll)
    {
    }

    internal NetworkMetricSource(IClock clock, Func<IReadOnlyList<IpHelperInterop.MIB_IF_ROW2>> reader)
    {
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _reader = reader ?? throw new ArgumentNullException(nameof(reader));
        _selectedLuids = ImmutableHashSet<ulong>.Empty;
    }

    public void SetSelectedAdapters(IEnumerable<ulong> luids)
    {
        ArgumentNullException.ThrowIfNull(luids);
        _selectedLuids = luids.ToImmutableHashSet();
    }

    public ValueTask<NetworkSnapshot> SampleAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<IpHelperInterop.MIB_IF_ROW2> rows;
        try
        {
            rows = _reader();
        }
        catch
        {
            return ValueTask.FromResult(NetworkSnapshot.Unavailable());
        }
        if (rows.Count == 0)
        {
            return ValueTask.FromResult(NetworkSnapshot.Unavailable());
        }

        long ticksNow = _clock.GetTimestampTicks();
        double ticksPerSecond = 1d / _clock.TicksToSeconds(1);

        var previous = new HashSet<ulong>(_state.Keys);
        var adapters = ImmutableArray.CreateBuilder<NetworkAdapterSnapshot>();
        double aggregateRx = 0;
        double aggregateTx = 0;
        bool anySelected = false;
        bool anySelectedOk = false;

        ImmutableHashSet<ulong> selectedLuids = _selectedLuids;
        bool autoMode = selectedLuids.Count == 0;
        bool hardwareMode = autoMode && HasHardwareUp(rows);

        foreach (var row in rows)
        {
            if (IsLoopback(row))
            {
                continue;
            }
            previous.Remove(row.InterfaceLuid);
            bool configured = autoMode
                ? hardwareMode ? IsHardware(row) : !IsFilter(row)
                : selectedLuids.Contains(row.InterfaceLuid);
            bool selected = configured && row.OperStatus == IpHelperInterop.IF_OPER_STATUS_UP;
            anySelected |= selected;
            if (!_state.TryGetValue(row.InterfaceLuid, out PerAdapterState? st))
            {
                st = new PerAdapterState();
                _state[row.InterfaceLuid] = st;
            }
            RateSample rxSample = UpdateCounter(st.RxRate, row.InOctets, ticksNow, ticksPerSecond);
            RateSample txSample = UpdateCounter(st.TxRate, row.OutOctets, ticksNow, ticksPerSecond);
            MetricStatus status = rxSample.IsWarmingUp || txSample.IsWarmingUp
                ? MetricStatus.WarmingUp
                : MetricStatus.Ok;
            if (status == MetricStatus.Ok && selected)
            {
                anySelectedOk = true;
                aggregateRx += rxSample.RatePerSecond;
                aggregateTx += txSample.RatePerSecond;
            }
            adapters.Add(new NetworkAdapterSnapshot
            {
                InterfaceLuid = row.InterfaceLuid,
                DisplayName = row.GetAlias(),
                Status = status,
                BytesReceivedPerSecond = rxSample.IsWarmingUp ? 0 : rxSample.RatePerSecond,
                BytesSentPerSecond = txSample.IsWarmingUp ? 0 : txSample.RatePerSecond,
                SessionBytesReceived = st.RxRate.SessionAccumulated,
                SessionBytesSent = st.TxRate.SessionAccumulated,
                IsSelected = configured,
            });
        }

        foreach (ulong stale in previous)
        {
            _state.Remove(stale);
        }

        return ValueTask.FromResult(new NetworkSnapshot
        {
            Adapters = adapters.ToImmutable(),
            AggregateStatus = !anySelected
                ? MetricStatus.Unavailable
                : anySelectedOk ? MetricStatus.Ok : MetricStatus.WarmingUp,
            AggregateBytesReceivedPerSecond = aggregateRx,
            AggregateBytesSentPerSecond = aggregateTx,
        });
    }

    public void ResetBaseline()
    {
        foreach (var st in _state.Values)
        {
            st.RxRate.Reset();
            st.TxRate.Reset();
        }
    }

    public void Dispose()
    {
        _state.Clear();
    }

    private static bool IsLoopback(in IpHelperInterop.MIB_IF_ROW2 row) =>
        row.Type == IpHelperInterop.IF_TYPE_SOFTWARE_LOOPBACK;

    private static bool IsFilter(in IpHelperInterop.MIB_IF_ROW2 row) =>
        (row.InterfaceAndOperStatusFlags & IpHelperInterop.IF_FLAG_FILTER_INTERFACE) != 0;

    // A row that is both Hardware and Filter counts as a filter row.
    private static bool IsHardware(in IpHelperInterop.MIB_IF_ROW2 row) =>
        (row.InterfaceAndOperStatusFlags & IpHelperInterop.IF_FLAG_HARDWARE_INTERFACE) != 0 && !IsFilter(row);

    private static bool HasHardwareUp(IReadOnlyList<IpHelperInterop.MIB_IF_ROW2> rows)
    {
        foreach (var row in rows)
        {
            if (!IsLoopback(row)
                && row.OperStatus == IpHelperInterop.IF_OPER_STATUS_UP
                && IsHardware(row))
            {
                return true;
            }
        }
        return false;
    }

    private static RateSample UpdateCounter(
        RateCalculator calculator,
        ulong currentValue,
        long timestampTicks,
        double ticksPerSecond)
    {
        if (currentValue > long.MaxValue)
        {
            calculator.Reset();
            return RateSample.WarmingUp;
        }

        return calculator.Update((long)currentValue, timestampTicks, ticksPerSecond);
    }

    private sealed class PerAdapterState
    {
        public RateCalculator RxRate { get; } = new();
        public RateCalculator TxRate { get; } = new();
    }
}
