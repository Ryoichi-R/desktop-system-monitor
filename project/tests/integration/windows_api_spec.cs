using DesktopSystemMonitor.Windows.Cpu;
using DesktopSystemMonitor.Windows.Gpu;
using DesktopSystemMonitor.Windows.Memory;
using DesktopSystemMonitor.Windows.Network;
using DesktopSystemMonitor.Windows.Processes;
using DesktopSystemMonitor.Core.Metrics;
using DesktopSystemMonitor.Core.Utility;
using System.Diagnostics;
using Xunit;

namespace DesktopSystemMonitor.IntegrationTests;

[Trait("Category", "WindowsIntegration")]
public sealed class WindowsApiTests
{
    [Fact]
    public void ip_helper_table_has_unique_luids_and_readable_aliases()
    {
        IReadOnlyList<IpHelperInterop.MIB_IF_ROW2> rows = IpHelperInterop.ReadAll();
        Assert.NotEmpty(rows);
        Assert.Equal(rows.Count, rows.Select(row => row.InterfaceLuid).Distinct().Count());
        Assert.All(rows, row => Assert.DoesNotContain('\0', row.GetAlias()));
    }

    [Fact]
    public async Task network_auto_mode_selects_exactly_the_expected_rows_of_the_live_table()
    {
        // One table read, shared by the product code and the expectation below.
        IReadOnlyList<IpHelperInterop.MIB_IF_ROW2> rows = IpHelperInterop.ReadAll();
        using var source = new NetworkMetricSource(new SystemClock(), () => rows);

        NetworkSnapshot snapshot = await source.SampleAsync(default);

        // The rule is restated here from the specification, not taken from the product.
        // InterfaceAndOperStatusFlags: bit0 = HardwareInterface, bit1 = FilterInterface.
        static bool Hardware(IpHelperInterop.MIB_IF_ROW2 row) => (row.InterfaceAndOperStatusFlags & 0x01) != 0;
        static bool Filter(IpHelperInterop.MIB_IF_ROW2 row) => (row.InterfaceAndOperStatusFlags & 0x02) != 0;

        List<IpHelperInterop.MIB_IF_ROW2> candidates = rows
            .Where(row => row.Type != IpHelperInterop.IF_TYPE_SOFTWARE_LOOPBACK)
            .ToList();
        bool hardwareUp = candidates.Any(row =>
            row.OperStatus == IpHelperInterop.IF_OPER_STATUS_UP && Hardware(row) && !Filter(row));
        HashSet<ulong> expected = candidates
            .Where(row => hardwareUp ? Hardware(row) && !Filter(row) : !Filter(row))
            .Select(row => row.InterfaceLuid)
            .ToHashSet();

        HashSet<ulong> actual = snapshot.Adapters
            .Where(adapter => adapter.IsSelected)
            .Select(adapter => adapter.InterfaceLuid)
            .ToHashSet();

        // Exact match catches both over-selection and missed rows. With no network the
        // expected set is empty and this still holds.
        Assert.True(
            expected.SetEquals(actual),
            $"Expected {expected.Count} selected row(s), product selected {actual.Count} (hardwareUp={hardwareUp}).");
        Assert.DoesNotContain(
            rows.Where(Filter),
            row => actual.Contains(row.InterfaceLuid));
    }

    [Fact]
    public void processor_power_information_call_is_safe()
    {
        uint maxMhz = ProcessorPowerInformation.GetMaxMhz();
        Assert.InRange(maxMhz, 0U, 20_000U);
    }

    [Fact]
    public async Task global_memory_status_returns_sane_physical_memory()
    {
        using var source = new MemoryMetricSource();

        MemorySnapshot snapshot = await source.SampleAsync(default);

        Assert.Equal(MetricStatus.Ok, snapshot.Status);
        Assert.True(snapshot.TotalBytes > 0);
        Assert.InRange(snapshot.UsedBytes, 0, snapshot.TotalBytes);
        Assert.InRange(snapshot.UtilizationPercent, 0, 100);
    }

    [Fact]
    public void dxgi_enumeration_returns_sane_adapter_metadata()
    {
        IReadOnlyList<DxgiAdapterInfo> adapters = DxgiAdapterEnumerator.Enumerate();
        Assert.Equal(adapters.Count, adapters.Select(adapter => adapter.Luid).Distinct().Count());
        Assert.All(adapters, adapter =>
        {
            Assert.False(string.IsNullOrWhiteSpace(adapter.Description));
            Assert.True(adapter.DedicatedVideoMemoryBytes >= 0);
        });
    }

    [Fact]
    public void private_working_set_reader_maps_the_current_process()
    {
        using Process process = Process.GetCurrentProcess();
        using var reader = new PrivateWorkingSetReader();

        reader.BeginSample();

        Assert.True(
            reader.TryGetBytes(process, out long bytes),
            $"No private working set provider succeeded. Selected provider: {reader.ProviderName}");
        Assert.True(bytes > 0);
    }
}
