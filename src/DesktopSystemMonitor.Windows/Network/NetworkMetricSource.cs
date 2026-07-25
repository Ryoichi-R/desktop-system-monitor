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
/// aggregated; otherwise every non-loopback interface with OperStatus Up is
/// summed.
/// </summary>
public interface INetworkMetricSource : IMetricSource<NetworkSnapshot>
{
    void SetSelectedAdapters(IEnumerable<ulong> luids);
}

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

        foreach (var row in rows)
        {
            if (row.Type == IpHelperInterop.IF_TYPE_SOFTWARE_LOOPBACK)
            {
                continue;
            }
            previous.Remove(row.InterfaceLuid);
            bool configured = _selectedLuids.Count == 0 || _selectedLuids.Contains(row.InterfaceLuid);
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
