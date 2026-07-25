using DesktopSystemMonitor.Core.Metrics;
using DesktopSystemMonitor.Core.Utility;
using DesktopSystemMonitor.Windows.Network;
using Xunit;

namespace DesktopSystemMonitor.Windows.Tests.Network;

[Trait("Category", "WindowsUnit")]
public class NetworkMetricSourceTests
{
    private static IpHelperInterop.MIB_IF_ROW2 MakeRow(ulong luid, ulong inOctets, ulong outOctets, uint type = 6, int oper = 1)
    {
        var row = default(IpHelperInterop.MIB_IF_ROW2);
        row.InterfaceLuid = luid;
        row.InOctets = inOctets;
        row.OutOctets = outOctets;
        row.Type = type;
        row.OperStatus = oper;
        return row;
    }

    [Fact]
    public async Task first_sample_is_warmup_and_second_sample_reports_rates()
    {
        var clock = new FakeClock();
        var rows1 = new[] { MakeRow(1, 1_000, 500) };
        var rows2 = new[] { MakeRow(1, 3_000, 1_500) };
        var queue = new Queue<IpHelperInterop.MIB_IF_ROW2[]>(new[] { rows1, rows2 });
        var source = new NetworkMetricSource(clock, () => queue.Dequeue());

        var first = await source.SampleAsync(default);
        Assert.Equal(MetricStatus.WarmingUp, first.AggregateStatus);

        clock.Advance(TimeSpan.FromSeconds(1));
        var second = await source.SampleAsync(default);
        Assert.Equal(MetricStatus.Ok, second.AggregateStatus);
        Assert.Equal(2_000d, second.AggregateBytesReceivedPerSecond, precision: 3);
        Assert.Equal(1_000d, second.AggregateBytesSentPerSecond, precision: 3);
    }

    [Fact]
    public async Task loopback_interfaces_are_never_counted()
    {
        var clock = new FakeClock();
        var rows1 = new[]
        {
            MakeRow(1, 1_000, 500, type: IpHelperInterop.IF_TYPE_SOFTWARE_LOOPBACK),
            MakeRow(2, 10_000, 5_000),
        };
        var rows2 = new[]
        {
            MakeRow(1, 999_999, 999_999, type: IpHelperInterop.IF_TYPE_SOFTWARE_LOOPBACK),
            MakeRow(2, 20_000, 15_000),
        };
        var queue = new Queue<IpHelperInterop.MIB_IF_ROW2[]>(new[] { rows1, rows2 });
        var source = new NetworkMetricSource(clock, () => queue.Dequeue());

        _ = await source.SampleAsync(default);
        clock.Advance(TimeSpan.FromSeconds(1));
        var second = await source.SampleAsync(default);
        Assert.Equal(10_000d, second.AggregateBytesReceivedPerSecond, precision: 3);
        Assert.Equal(10_000d, second.AggregateBytesSentPerSecond, precision: 3);
    }

    [Fact]
    public async Task selected_luids_shrink_the_aggregate()
    {
        var clock = new FakeClock();
        var rows1 = new[] { MakeRow(1, 1_000, 500), MakeRow(2, 5_000, 2_500) };
        var rows2 = new[] { MakeRow(1, 2_000, 1_000), MakeRow(2, 10_000, 5_000) };
        var queue = new Queue<IpHelperInterop.MIB_IF_ROW2[]>(new[] { rows1, rows2 });
        var source = new NetworkMetricSource(clock, () => queue.Dequeue());
        source.SetSelectedAdapters(new ulong[] { 1 });

        _ = await source.SampleAsync(default);
        clock.Advance(TimeSpan.FromSeconds(1));
        var second = await source.SampleAsync(default);
        Assert.Equal(1_000d, second.AggregateBytesReceivedPerSecond, precision: 3);
        Assert.Equal(500d, second.AggregateBytesSentPerSecond, precision: 3);
    }

    [Fact]
    public async Task no_up_adapter_is_unavailable_in_auto_mode()
    {
        var clock = new FakeClock();
        var source = new NetworkMetricSource(clock, () => [MakeRow(1, 1_000, 500, oper: 2)]);
        NetworkSnapshot snapshot = await source.SampleAsync(default);
        Assert.Equal(MetricStatus.Unavailable, snapshot.AggregateStatus);
    }

    [Fact]
    public async Task loopback_only_table_is_unavailable()
    {
        var clock = new FakeClock();
        var source = new NetworkMetricSource(clock, () =>
            [MakeRow(1, 1_000, 500, type: IpHelperInterop.IF_TYPE_SOFTWARE_LOOPBACK)]);
        NetworkSnapshot snapshot = await source.SampleAsync(default);
        Assert.Equal(MetricStatus.Unavailable, snapshot.AggregateStatus);
        Assert.Empty(snapshot.Adapters);
    }

    [Fact]
    public async Task explicitly_selected_but_down_adapter_is_unavailable()
    {
        var clock = new FakeClock();
        var source = new NetworkMetricSource(clock, () => [MakeRow(7, 1_000, 500, oper: 2)]);
        source.SetSelectedAdapters([7]);
        NetworkSnapshot snapshot = await source.SampleAsync(default);
        Assert.Equal(MetricStatus.Unavailable, snapshot.AggregateStatus);
        Assert.True(Assert.Single(snapshot.Adapters).IsSelected);
    }

    [Fact]
    public async Task counter_above_signed_range_warms_only_that_adapter()
    {
        var clock = new FakeClock();
        var queue = new Queue<IpHelperInterop.MIB_IF_ROW2[]>(
        [
            [MakeRow(1, 1_000, 500), MakeRow(2, 2_000, 1_000)],
            [MakeRow(1, (ulong)long.MaxValue + 1, (ulong)long.MaxValue + 1), MakeRow(2, 3_000, 1_500)],
        ]);
        using var source = new NetworkMetricSource(clock, () => queue.Dequeue());

        _ = await source.SampleAsync(default);
        clock.Advance(TimeSpan.FromSeconds(1));
        NetworkSnapshot second = await source.SampleAsync(default);

        Assert.Equal(MetricStatus.Ok, second.AggregateStatus);
        Assert.Equal(1_000d, second.AggregateBytesReceivedPerSecond, precision: 3);
        Assert.Equal(500d, second.AggregateBytesSentPerSecond, precision: 3);
        NetworkAdapterSnapshot overflowed = Assert.Single(second.Adapters, adapter => adapter.InterfaceLuid == 1);
        Assert.Equal(MetricStatus.WarmingUp, overflowed.Status);
    }

    private sealed class FakeClock : IClock
    {
        private long _ticks;
        public DateTimeOffset UtcNow { get; private set; } = DateTimeOffset.UtcNow;
        public long GetTimestampTicks() => _ticks;
        public double TicksToSeconds(long deltaTicks) => deltaTicks / 10_000_000d;
        public void Advance(TimeSpan span)
        {
            _ticks += (long)(span.TotalSeconds * 10_000_000d);
            UtcNow = UtcNow.Add(span);
        }
    }
}
