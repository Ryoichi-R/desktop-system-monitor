using DesktopSystemMonitor.Core.Metrics;
using DesktopSystemMonitor.Core.Utility;
using DesktopSystemMonitor.Windows.Network;
using Xunit;

namespace DesktopSystemMonitor.Windows.Tests.Network;

[Trait("Category", "WindowsUnit")]
public class NetworkMetricSourceTests
{
    // InterfaceAndOperStatusFlags patterns seen on a real machine.
    private const byte PhysicalFlags = 0x05;   // HardwareInterface | ConnectorPresent
    private const byte FilterFlags = 0x02;     // FilterInterface (NDIS filter / intermediate driver)
    private const byte HardwareAndFilterFlags = 0x03;
    private const byte VirtualFlags = 0x00;    // neither Hardware nor Filter (tunnel, vEthernet, ...)

    private static IpHelperInterop.MIB_IF_ROW2 MakeRow(
        ulong luid,
        ulong inOctets,
        ulong outOctets,
        uint type = 6,
        int oper = 1,
        byte flags = IpHelperInterop.IF_FLAG_HARDWARE_INTERFACE)
    {
        var row = default(IpHelperInterop.MIB_IF_ROW2);
        row.InterfaceLuid = luid;
        row.InOctets = inOctets;
        row.OutOctets = outOctets;
        row.Type = type;
        row.OperStatus = oper;
        row.InterfaceAndOperStatusFlags = flags;
        return row;
    }

    /// <summary>Feeds one table per sample, advancing the clock one second between samples.</summary>
    private sealed class Harness
    {
        private readonly FakeClock _clock = new();
        private readonly Queue<IpHelperInterop.MIB_IF_ROW2[]> _tables;
        private bool _sampled;

        public Harness(params IpHelperInterop.MIB_IF_ROW2[][] tables)
        {
            _tables = new Queue<IpHelperInterop.MIB_IF_ROW2[]>(tables);
            Source = new NetworkMetricSource(_clock, () => _tables.Dequeue());
        }

        public NetworkMetricSource Source { get; }

        public async Task<NetworkSnapshot> NextAsync()
        {
            if (_sampled)
            {
                _clock.Advance(TimeSpan.FromSeconds(1));
            }
            _sampled = true;
            return await Source.SampleAsync(default);
        }

        public async Task<NetworkSnapshot> LastOfAsync(int samples)
        {
            NetworkSnapshot last = default!;
            for (int i = 0; i < samples; i++)
            {
                last = await NextAsync();
            }
            return last;
        }
    }

    private static bool IsSelected(NetworkSnapshot snapshot, ulong luid) =>
        Assert.Single(snapshot.Adapters, adapter => adapter.InterfaceLuid == luid).IsSelected;

    // ---- selection rule -------------------------------------------------

    [Fact]
    public async Task filter_rows_are_not_added_on_top_of_the_physical_row()
    {
        const ulong Physical = 1;
        var t0 = new List<IpHelperInterop.MIB_IF_ROW2> { MakeRow(Physical, 0, 0, flags: PhysicalFlags) };
        var t1 = new List<IpHelperInterop.MIB_IF_ROW2> { MakeRow(Physical, 7_125_000, 200_000, flags: PhysicalFlags) };
        for (ulong luid = 10; luid < 16; luid++)
        {
            t0.Add(MakeRow(luid, 0, 0, flags: FilterFlags));
            t1.Add(MakeRow(luid, 7_125_000, 200_000, flags: FilterFlags));
        }
        var harness = new Harness(t0.ToArray(), t1.ToArray());

        NetworkSnapshot second = await harness.LastOfAsync(2);

        Assert.Equal(MetricStatus.Ok, second.AggregateStatus);
        Assert.Equal(7_125_000d, second.AggregateBytesReceivedPerSecond, precision: 3);
        Assert.Equal(200_000d, second.AggregateBytesSentPerSecond, precision: 3);
        Assert.True(IsSelected(second, Physical));
        for (ulong luid = 10; luid < 16; luid++)
        {
            Assert.False(IsSelected(second, luid));
        }
    }

    [Fact]
    public async Task hardware_rows_win_over_virtual_rows()
    {
        var harness = new Harness(
            [MakeRow(1, 0, 0, flags: PhysicalFlags), MakeRow(2, 0, 0, flags: VirtualFlags)],
            [MakeRow(1, 1_000, 500, flags: PhysicalFlags), MakeRow(2, 4_000, 2_000, flags: VirtualFlags)]);

        NetworkSnapshot second = await harness.LastOfAsync(2);

        Assert.Equal(1_000d, second.AggregateBytesReceivedPerSecond, precision: 3);
        Assert.Equal(500d, second.AggregateBytesSentPerSecond, precision: 3);
        Assert.True(IsSelected(second, 1));
        Assert.False(IsSelected(second, 2));
    }

    [Fact]
    public async Task several_up_hardware_rows_are_summed()
    {
        var harness = new Harness(
            [MakeRow(1, 0, 0, flags: PhysicalFlags), MakeRow(2, 0, 0, flags: PhysicalFlags)],
            [MakeRow(1, 1_000, 500, flags: PhysicalFlags), MakeRow(2, 3_000, 1_500, flags: PhysicalFlags)]);

        NetworkSnapshot second = await harness.LastOfAsync(2);

        Assert.Equal(4_000d, second.AggregateBytesReceivedPerSecond, precision: 3);
        Assert.Equal(2_000d, second.AggregateBytesSentPerSecond, precision: 3);
    }

    [Fact]
    public async Task down_hardware_row_stays_selected_but_is_not_aggregated()
    {
        var harness = new Harness(
            [MakeRow(1, 0, 0, flags: PhysicalFlags), MakeRow(2, 0, 0, oper: 2, flags: PhysicalFlags)],
            [MakeRow(1, 1_000, 500, flags: PhysicalFlags), MakeRow(2, 9_000, 9_000, oper: 2, flags: PhysicalFlags)]);

        NetworkSnapshot second = await harness.LastOfAsync(2);

        Assert.Equal(1_000d, second.AggregateBytesReceivedPerSecond, precision: 3);
        Assert.Equal(500d, second.AggregateBytesSentPerSecond, precision: 3);
        Assert.True(IsSelected(second, 2));
    }

    [Fact]
    public async Task fallback_without_hardware_row_sums_only_non_filter_rows()
    {
        var harness = new Harness(
            [MakeRow(1, 0, 0, flags: VirtualFlags), MakeRow(2, 0, 0, flags: FilterFlags)],
            [MakeRow(1, 1_000, 500, flags: VirtualFlags), MakeRow(2, 1_000, 500, flags: FilterFlags)]);

        NetworkSnapshot second = await harness.LastOfAsync(2);

        Assert.Equal(MetricStatus.Ok, second.AggregateStatus);
        Assert.Equal(1_000d, second.AggregateBytesReceivedPerSecond, precision: 3);
        Assert.Equal(500d, second.AggregateBytesSentPerSecond, precision: 3);
        Assert.True(IsSelected(second, 1));
        Assert.False(IsSelected(second, 2));
    }

    // Documents a known limitation: in fallback mode, several non-filter rows that
    // carry the same traffic cannot be told apart, so they are all summed.
    [Fact]
    public async Task fallback_sums_several_non_filter_rows_even_when_they_overlap()
    {
        var harness = new Harness(
            [MakeRow(1, 0, 0, flags: VirtualFlags), MakeRow(2, 0, 0, flags: VirtualFlags)],
            [MakeRow(1, 1_000, 500, flags: VirtualFlags), MakeRow(2, 1_000, 500, flags: VirtualFlags)]);

        NetworkSnapshot second = await harness.LastOfAsync(2);

        Assert.Equal(2_000d, second.AggregateBytesReceivedPerSecond, precision: 3);
        Assert.Equal(1_000d, second.AggregateBytesSentPerSecond, precision: 3);
    }

    [Fact]
    public async Task table_with_only_filter_rows_is_unavailable()
    {
        var harness = new Harness([MakeRow(1, 1_000, 500, flags: FilterFlags), MakeRow(2, 2_000, 900, flags: FilterFlags)]);

        NetworkSnapshot snapshot = await harness.NextAsync();

        Assert.Equal(MetricStatus.Unavailable, snapshot.AggregateStatus);
        Assert.Equal(2, snapshot.Adapters.Length);
        Assert.All(snapshot.Adapters, adapter => Assert.False(adapter.IsSelected));
    }

    [Fact]
    public async Task row_with_hardware_and_filter_bits_is_treated_as_a_filter_row()
    {
        var harness = new Harness(
            [MakeRow(1, 0, 0, flags: HardwareAndFilterFlags), MakeRow(2, 0, 0, flags: VirtualFlags)],
            [MakeRow(1, 9_000, 9_000, flags: HardwareAndFilterFlags), MakeRow(2, 1_000, 500, flags: VirtualFlags)]);

        NetworkSnapshot second = await harness.LastOfAsync(2);

        // Not hardware mode (the 0x03 row must not count as hardware): fallback picks
        // the non-filter row only.
        Assert.Equal(MetricStatus.Ok, second.AggregateStatus);
        Assert.Equal(1_000d, second.AggregateBytesReceivedPerSecond, precision: 3);
        Assert.Equal(500d, second.AggregateBytesSentPerSecond, precision: 3);
        Assert.False(IsSelected(second, 1));
        Assert.True(IsSelected(second, 2));
    }

    [Fact]
    public async Task loopback_row_with_hardware_bit_does_not_trigger_hardware_mode()
    {
        var harness = new Harness(
            [
                MakeRow(1, 0, 0, type: IpHelperInterop.IF_TYPE_SOFTWARE_LOOPBACK, flags: PhysicalFlags),
                MakeRow(2, 0, 0, flags: VirtualFlags),
            ],
            [
                MakeRow(1, 9_000, 9_000, type: IpHelperInterop.IF_TYPE_SOFTWARE_LOOPBACK, flags: PhysicalFlags),
                MakeRow(2, 1_000, 500, flags: VirtualFlags),
            ]);

        NetworkSnapshot second = await harness.LastOfAsync(2);

        Assert.Equal(MetricStatus.Ok, second.AggregateStatus);
        Assert.Equal(1_000d, second.AggregateBytesReceivedPerSecond, precision: 3);
        Assert.Equal(500d, second.AggregateBytesSentPerSecond, precision: 3);
        Assert.DoesNotContain(second.Adapters, adapter => adapter.InterfaceLuid == 1);
    }

    [Theory]
    [InlineData(2UL, 4_000d, 2_000d)]   // virtual row
    [InlineData(3UL, 7_000d, 3_500d)]   // filter row
    public async Task explicit_selection_overrides_the_flags(ulong luid, double expectedRx, double expectedTx)
    {
        var harness = new Harness(
            [
                MakeRow(1, 0, 0, flags: PhysicalFlags),
                MakeRow(2, 0, 0, flags: VirtualFlags),
                MakeRow(3, 0, 0, flags: FilterFlags),
            ],
            [
                MakeRow(1, 1_000, 500, flags: PhysicalFlags),
                MakeRow(2, 4_000, 2_000, flags: VirtualFlags),
                MakeRow(3, 7_000, 3_500, flags: FilterFlags),
            ]);
        harness.Source.SetSelectedAdapters([luid]);

        NetworkSnapshot second = await harness.LastOfAsync(2);

        Assert.Equal(expectedRx, second.AggregateBytesReceivedPerSecond, precision: 3);
        Assert.Equal(expectedTx, second.AggregateBytesSentPerSecond, precision: 3);
        Assert.True(IsSelected(second, luid));
        Assert.False(IsSelected(second, 1));
    }

    // ---- switching and warm-up ------------------------------------------

    [Fact]
    public async Task hardware_row_going_down_switches_to_fallback_without_warming_up()
    {
        var harness = new Harness(
            [MakeRow(1, 0, 0, flags: PhysicalFlags), MakeRow(2, 0, 0, flags: VirtualFlags)],
            [MakeRow(1, 5_000, 2_500, flags: PhysicalFlags), MakeRow(2, 1_000, 500, flags: VirtualFlags)],
            [MakeRow(1, 10_000, 5_000, oper: 2, flags: PhysicalFlags), MakeRow(2, 2_000, 1_000, flags: VirtualFlags)]);

        NetworkSnapshot first = await harness.NextAsync();
        Assert.Equal(MetricStatus.WarmingUp, first.AggregateStatus);
        NetworkSnapshot second = await harness.NextAsync();
        Assert.Equal(5_000d, second.AggregateBytesReceivedPerSecond, precision: 3);
        NetworkSnapshot third = await harness.NextAsync();

        Assert.Equal(MetricStatus.Ok, third.AggregateStatus);
        Assert.Equal(1_000d, third.AggregateBytesReceivedPerSecond, precision: 3);
        Assert.Equal(500d, third.AggregateBytesSentPerSecond, precision: 3);
    }

    [Fact]
    public async Task hardware_row_coming_up_switches_back_without_warming_up()
    {
        var harness = new Harness(
            [MakeRow(1, 0, 0, oper: 2, flags: PhysicalFlags), MakeRow(2, 0, 0, flags: VirtualFlags)],
            [MakeRow(1, 5_000, 2_500, oper: 2, flags: PhysicalFlags), MakeRow(2, 1_000, 500, flags: VirtualFlags)],
            [MakeRow(1, 10_000, 5_000, flags: PhysicalFlags), MakeRow(2, 2_000, 1_000, flags: VirtualFlags)]);

        _ = await harness.NextAsync();
        NetworkSnapshot second = await harness.NextAsync();
        Assert.Equal(1_000d, second.AggregateBytesReceivedPerSecond, precision: 3);
        NetworkSnapshot third = await harness.NextAsync();

        Assert.Equal(MetricStatus.Ok, third.AggregateStatus);
        Assert.Equal(5_000d, third.AggregateBytesReceivedPerSecond, precision: 3);
        Assert.Equal(2_500d, third.AggregateBytesSentPerSecond, precision: 3);
    }

    [Fact]
    public async Task reappearing_row_warms_up_once_and_never_spikes()
    {
        var harness = new Harness(
            [MakeRow(1, 0, 0, flags: PhysicalFlags), MakeRow(2, 0, 0, flags: VirtualFlags)],
            [MakeRow(1, 5_000, 2_500, flags: PhysicalFlags), MakeRow(2, 1_000, 500, flags: VirtualFlags)],
            [MakeRow(2, 2_000, 1_000, flags: VirtualFlags)],
            [MakeRow(1, 20_000, 10_000, flags: PhysicalFlags), MakeRow(2, 3_000, 1_500, flags: VirtualFlags)],
            [MakeRow(1, 25_000, 12_500, flags: PhysicalFlags), MakeRow(2, 4_000, 2_000, flags: VirtualFlags)]);

        _ = await harness.NextAsync();
        NetworkSnapshot hardwareOnly = await harness.NextAsync();
        NetworkSnapshot rowGone = await harness.NextAsync();
        NetworkSnapshot reappeared = await harness.NextAsync();
        NetworkSnapshot recovered = await harness.NextAsync();

        Assert.Equal(5_000d, hardwareOnly.AggregateBytesReceivedPerSecond, precision: 3);
        // Hardware row absent: fallback sums the virtual row (its own state is intact).
        Assert.Equal(MetricStatus.Ok, rowGone.AggregateStatus);
        Assert.Equal(1_000d, rowGone.AggregateBytesReceivedPerSecond, precision: 3);
        // Re-appearance: the row starts over, so nothing is aggregated, and the
        // accumulated counter (20_000) is not turned into a rate.
        Assert.Equal(MetricStatus.WarmingUp, reappeared.AggregateStatus);
        Assert.Equal(0d, reappeared.AggregateBytesReceivedPerSecond, precision: 3);
        Assert.Equal(0d, reappeared.AggregateBytesSentPerSecond, precision: 3);
        Assert.Equal(MetricStatus.Ok, recovered.AggregateStatus);
        Assert.Equal(5_000d, recovered.AggregateBytesReceivedPerSecond, precision: 3);
        Assert.Equal(2_500d, recovered.AggregateBytesSentPerSecond, precision: 3);
    }

    [Fact]
    public async Task new_hardware_row_does_not_spike_the_total()
    {
        const ulong HugeCounter = 10_000_000_000;
        var harness = new Harness(
            [MakeRow(1, 0, 0, flags: PhysicalFlags)],
            [MakeRow(1, 1_000, 500, flags: PhysicalFlags)],
            [
                MakeRow(1, 2_000, 1_000, flags: PhysicalFlags),
                MakeRow(2, HugeCounter, HugeCounter, flags: PhysicalFlags),
            ],
            [
                MakeRow(1, 3_000, 1_500, flags: PhysicalFlags),
                MakeRow(2, HugeCounter + 4_000, HugeCounter + 2_000, flags: PhysicalFlags),
            ]);

        _ = await harness.NextAsync();
        _ = await harness.NextAsync();
        NetworkSnapshot appeared = await harness.NextAsync();
        NetworkSnapshot both = await harness.NextAsync();

        Assert.Equal(MetricStatus.Ok, appeared.AggregateStatus);
        Assert.Equal(1_000d, appeared.AggregateBytesReceivedPerSecond, precision: 3);
        Assert.Equal(500d, appeared.AggregateBytesSentPerSecond, precision: 3);
        Assert.Equal(5_000d, both.AggregateBytesReceivedPerSecond, precision: 3);
        Assert.Equal(2_500d, both.AggregateBytesSentPerSecond, precision: 3);
    }

    [Fact]
    public async Task counter_decrease_on_the_selected_row_warms_up_and_recovers()
    {
        var harness = new Harness(
            [MakeRow(1, 10_000, 10_000, flags: PhysicalFlags)],
            [MakeRow(1, 20_000, 20_000, flags: PhysicalFlags)],
            [MakeRow(1, 500, 500, flags: PhysicalFlags)],
            [MakeRow(1, 1_500, 1_500, flags: PhysicalFlags)]);

        _ = await harness.NextAsync();
        NetworkSnapshot running = await harness.NextAsync();
        NetworkSnapshot reset = await harness.NextAsync();
        NetworkSnapshot recovered = await harness.NextAsync();

        Assert.Equal(10_000d, running.AggregateBytesReceivedPerSecond, precision: 3);
        Assert.Equal(MetricStatus.WarmingUp, reset.AggregateStatus);
        Assert.Equal(0d, reset.AggregateBytesReceivedPerSecond, precision: 3);
        Assert.Equal(0d, reset.AggregateBytesSentPerSecond, precision: 3);
        Assert.Equal(MetricStatus.Ok, recovered.AggregateStatus);
        Assert.Equal(1_000d, recovered.AggregateBytesReceivedPerSecond, precision: 3);
        Assert.Equal(1_000d, recovered.AggregateBytesSentPerSecond, precision: 3);
    }

    // ---- existing behaviour ---------------------------------------------

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
