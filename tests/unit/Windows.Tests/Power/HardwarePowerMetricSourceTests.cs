using System.Runtime.InteropServices;
using DesktopSystemMonitor.Core.Metrics;
using DesktopSystemMonitor.Windows.Power;
using LibreHardwareMonitor.Hardware;
using Xunit;

namespace DesktopSystemMonitor.Windows.Tests.Power;

public sealed class HardwarePowerMetricSourceTests
{
    [Fact]
    public void temperature_selector_prefers_package_and_gpu_core()
    {
        TemperatureSnapshot snapshot = TemperatureSensorSelector.Select([
            new TemperatureSensorReading(HardwareType.Cpu, "cpu0", "CPU", "CPU Core #1", 70),
            new TemperatureSensorReading(HardwareType.Cpu, "cpu0", "CPU", "CPU Package", 62),
            new TemperatureSensorReading(HardwareType.GpuNvidia, "gpu0", "GPU", "GPU Hot Spot", 90),
            new TemperatureSensorReading(HardwareType.GpuNvidia, "gpu0", "GPU", "GPU Core", 65),
        ]);
        Assert.Equal(62, snapshot.CpuPackageCelsius);
        Assert.Single(snapshot.GpuReadings);
        Assert.Equal(65, snapshot.GpuReadings[0].Celsius);
    }
    [Fact]
    public void same_name_cpu_packages_are_selected_by_identifier_and_summed()
    {
        PowerSnapshot snapshot = PowerSensorSelector.Select(
        [
            new(HardwareType.Cpu, "/cpu/0", "Same CPU", "CPU Cores", 40),
            new(HardwareType.Cpu, "/cpu/0", "Same CPU", "CPU Package", 55.5f),
            new(HardwareType.Cpu, "/cpu/1", "Same CPU", "Package", 60.25f),
        ]);

        Assert.Equal(MetricStatus.Ok, snapshot.CpuPackageStatus);
        Assert.Equal(MetricStatus.Unavailable, snapshot.GpuCollectionStatus);
        Assert.Equal(115.75, snapshot.CpuPackageWatts, 3);
    }

    [Fact]
    public void total_board_power_has_priority_for_each_gpu()
    {
        PowerSnapshot snapshot = PowerSensorSelector.Select(
        [
            new(HardwareType.GpuNvidia, "/gpu-nvidia/0", "NVIDIA GeForce", "GPU Package", 80),
            new(HardwareType.GpuNvidia, "/gpu-nvidia/0", "NVIDIA GeForce", "GPU Total Board Power", 95),
            new(HardwareType.GpuAmd, "/gpu-amd/0", "AMD Radeon", "GPU PPT", 45),
        ]);

        Assert.Collection(
            snapshot.GpuReadings.OrderBy(reading => reading.DisplayName),
            amd =>
            {
                Assert.Equal("AMD Radeon", amd.DisplayName);
                Assert.Equal(45, amd.Watts);
            },
            nvidia =>
            {
                Assert.Equal("NVIDIA GeForce", nvidia.DisplayName);
                Assert.Equal(95, nvidia.Watts);
            });
    }

    [Fact]
    public void same_name_gpus_remain_separate_when_identifiers_differ()
    {
        PowerSnapshot snapshot = PowerSensorSelector.Select(
        [
            new(HardwareType.GpuNvidia, "/gpu-nvidia/0", "Same GPU", "GPU Package", 80),
            new(HardwareType.GpuNvidia, "/gpu-nvidia/1", "Same GPU", "GPU Package", 90),
        ]);

        Assert.Equal(2, snapshot.GpuReadings.Length);
        Assert.Equal([80d, 90d], snapshot.GpuReadings.Select(reading => reading.Watts).Order().ToArray());
    }

    [Theory]
    [InlineData(float.NaN)]
    [InlineData(-1f)]
    [InlineData(PowerSensorSelector.MaximumReasonableWatts + 0.1f)]
    public void invalid_or_implausible_power_is_unavailable(float watts)
    {
        PowerSnapshot snapshot = PowerSensorSelector.Select(
        [
            new(HardwareType.Cpu, "/cpu/0", "CPU", "CPU Cores", 20),
            new(HardwareType.GpuIntel, "/gpu-intel/0", "Intel Graphics", "GPU Package", watts),
        ]);

        Assert.Equal(MetricStatus.Unavailable, snapshot.CpuPackageStatus);
        Assert.Equal(MetricStatus.Ok, snapshot.GpuCollectionStatus);
        GpuPowerReading gpu = Assert.Single(snapshot.GpuReadings);
        Assert.Equal(MetricStatus.Unavailable, gpu.Status);
        Assert.True(double.IsNaN(gpu.Watts));
    }

    [Fact]
    public void zero_cpu_package_power_is_treated_as_unavailable()
    {
        PowerSnapshot snapshot = PowerSensorSelector.Select(
        [
            new(HardwareType.Cpu, "/cpu/0", "CPU", "CPU Package", 0),
        ]);

        Assert.Equal(MetricStatus.Unavailable, snapshot.CpuPackageStatus);
        Assert.True(double.IsNaN(snapshot.CpuPackageWatts));
    }

    [Theory]
    [InlineData(@"\Energy Meter(RAPL_Package0_PKG)\Power", true)]
    [InlineData(@"\Energy Meter(RAPL_Package0_pkg)\Power", true)]
    [InlineData(@"\Energy Meter(RAPL_Package0_DRAM)\Power", false)]
    [InlineData(@"\Energy Meter(RAPL_Package0_PP0)\Power", false)]
    [InlineData(@"\Energy Meter(RAPL_Package0_PKG)\Energy", false)]
    public void energy_meter_selects_only_package_power_paths(string path, bool expected)
    {
        Assert.Equal(expected, EnergyMeterCpuPowerReader.IsPackagePowerPath(path));
    }

    [Theory]
    [InlineData(@"\Energy Meter(CPU_CLUSTER_0)\Power", true)]
    [InlineData(@"\Energy Meter(cpu_cluster_00)\power", true)]
    [InlineData(@"\Energy Meter(CPU_CLUSTER_)\Power", false)]
    [InlineData(@"\Energy Meter(CPU_CLUSTER_-1)\Power", false)]
    [InlineData(@"\Energy Meter(CPU_CLUSTER_2147483648)\Power", false)]
    [InlineData(@"\Energy Meter(CPU_CLUSTER_X)\Power", false)]
    [InlineData(@"\Energy Meter(CPU_CLUSTER_0_EXTRA)\Power", false)]
    [InlineData(@"\Energy Meter(CPU_CLUSTER_0)\Energy", false)]
    [InlineData(@"\Energy Meter(SYS)\Power", false)]
    public void energy_meter_selects_only_cluster_power_paths(string path, bool expected)
    {
        Assert.Equal(expected, EnergyMeterCpuPowerReader.IsClusterPowerPath(path));
    }

    [Fact]
    public void package_paths_take_priority_over_cluster_paths()
    {
        const string package = @"\Energy Meter(RAPL_Package0_PKG)\Power";
        SelectedCpuPowerPaths selected = EnergyMeterCpuPowerReader.SelectPowerPaths(
        [
            @"\Energy Meter(CPU_CLUSTER_0)\Power",
            package,
            @"\Energy Meter(CPU_CLUSTER_1)\Power",
        ]);

        Assert.Equal(CpuPowerCounterKind.Package, selected.Kind);
        Assert.Equal([package], selected.Paths);
    }

    [Fact]
    public void cluster_aliases_are_deduplicated_by_numeric_index()
    {
        const string canonical0 = @"\Energy Meter(CPU_CLUSTER_0)\Power";
        const string cluster1 = @"\Energy Meter(CPU_CLUSTER_1)\Power";
        SelectedCpuPowerPaths selected = EnergyMeterCpuPowerReader.SelectPowerPaths(
        [
            @"\Energy Meter(cpu_cluster_00)\Power",
            cluster1,
            canonical0,
            @"\Energy Meter(CPU_CLUSTER_01)\Power",
            @"\Energy Meter(SYS)\Power",
            @"\Energy Meter(_Total)\Power",
            @"\Energy Meter(PSU_USB)\Power",
            @"\Energy Meter(USBC_TOTAL)\Power",
        ]);

        Assert.Equal(CpuPowerCounterKind.Cluster, selected.Kind);
        Assert.Equal([canonical0, cluster1], selected.Paths);
    }

    [Fact]
    public void noncanonical_cluster_name_is_used_when_it_is_the_only_alias()
    {
        const string cluster = @"\Energy Meter(CPU_CLUSTER_00)\Power";

        SelectedCpuPowerPaths selected = EnergyMeterCpuPowerReader.SelectPowerPaths([cluster]);

        Assert.Equal(CpuPowerCounterKind.Cluster, selected.Kind);
        Assert.Equal([cluster], selected.Paths);
    }

    [Fact]
    public void energy_meter_sums_packages_and_converts_milliwatts_to_watts()
    {
        const string package0 = @"\Energy Meter(RAPL_Package0_PKG)\Power";
        const string package1 = @"\Energy Meter(RAPL_Package1_PKG)\Power";
        using var query = new FakePowerCounterQuery(new Dictionary<string, double>
        {
            [package0] = 29_394.83,
            [package1] = 10_605.17,
        });
        using var reader = EnergyMeterCpuPowerReader.CreateForTest(
            query,
            [package0, @"\Energy Meter(RAPL_Package0_DRAM)\Power", package1]);

        CpuPowerReading reading = reader.Sample();

        Assert.Equal(MetricStatus.Ok, reading.Status);
        Assert.Equal(40, reading.Watts, 6);
        Assert.Equal([package0, package1], query.AddedPaths.Order().ToArray());
    }

    [Fact]
    public void energy_meter_sums_clusters_and_allows_one_zero_cluster()
    {
        const string cluster0 = @"\Energy Meter(CPU_CLUSTER_0)\Power";
        const string cluster1 = @"\Energy Meter(CPU_CLUSTER_1)\Power";
        using var query = new FakePowerCounterQuery(new Dictionary<string, double>
        {
            [cluster0] = 0,
            [cluster1] = 3_360,
        });
        using var reader = EnergyMeterCpuPowerReader.CreateForTest(
            query,
            [cluster1, @"\Energy Meter(SYS)\Power", cluster0]);

        CpuPowerReading reading = reader.Sample();

        Assert.Equal(MetricStatus.Ok, reading.Status);
        Assert.Equal(3.36, reading.Watts, 6);
        Assert.Equal([cluster0, cluster1], query.AddedPaths);
    }

    [Fact]
    public void energy_meter_rejects_all_zero_clusters()
    {
        const string cluster0 = @"\Energy Meter(CPU_CLUSTER_0)\Power";
        const string cluster1 = @"\Energy Meter(CPU_CLUSTER_1)\Power";
        using var query = new FakePowerCounterQuery(new Dictionary<string, double>
        {
            [cluster0] = 0,
            [cluster1] = 0,
        });
        using var reader = EnergyMeterCpuPowerReader.CreateForTest(query, [cluster0, cluster1]);

        CpuPowerReading reading = reader.Sample();

        Assert.Equal(MetricStatus.Unavailable, reading.Status);
        Assert.True(double.IsNaN(reading.Watts));
    }

    [Fact]
    public void energy_meter_rejects_cluster_total_over_maximum()
    {
        const string cluster0 = @"\Energy Meter(CPU_CLUSTER_0)\Power";
        const string cluster1 = @"\Energy Meter(CPU_CLUSTER_1)\Power";
        using var query = new FakePowerCounterQuery(new Dictionary<string, double>
        {
            [cluster0] = 3_000_000,
            [cluster1] = 3_000_000,
        });
        using var reader = EnergyMeterCpuPowerReader.CreateForTest(query, [cluster0, cluster1]);

        Assert.Equal(MetricStatus.Unavailable, reader.Sample().Status);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(EnergyMeterCpuPowerReader.MaximumReasonableMilliwatts + 1)]
    public void invalid_cluster_value_is_unavailable(double milliwatts)
    {
        const string cluster = @"\Energy Meter(CPU_CLUSTER_0)\Power";
        using var query = new FakePowerCounterQuery(new Dictionary<string, double>
        {
            [cluster] = milliwatts,
        });
        using var reader = EnergyMeterCpuPowerReader.CreateForTest(query, [cluster]);

        CpuPowerReading reading = reader.Sample();

        Assert.Equal(MetricStatus.Unavailable, reading.Status);
        Assert.True(double.IsNaN(reading.Watts));
    }

    [Fact]
    public void energy_meter_requires_every_cluster_reading()
    {
        const string cluster0 = @"\Energy Meter(CPU_CLUSTER_0)\Power";
        const string cluster1 = @"\Energy Meter(CPU_CLUSTER_1)\Power";
        using var query = new FakePowerCounterQuery(new Dictionary<string, double>
        {
            [cluster0] = 2_450,
            [cluster1] = 3_360,
        });
        query.FailedReads.Add(cluster1);
        using var reader = EnergyMeterCpuPowerReader.CreateForTest(query, [cluster0, cluster1]);

        Assert.Equal(MetricStatus.Unavailable, reader.Sample().Status);
    }

    [Fact]
    public void partial_cluster_initialization_disposes_owned_query()
    {
        const string cluster0 = @"\Energy Meter(CPU_CLUSTER_0)\Power";
        const string cluster1 = @"\Energy Meter(CPU_CLUSTER_1)\Power";
        var query = new FakePowerCounterQuery(new Dictionary<string, double>
        {
            [cluster0] = 2_450,
        });

        using ICpuPowerReader reader = EnergyMeterCpuPowerReader.TryCreateForTest(
            query,
            [cluster0, cluster1]);

        Assert.Same(UnavailableCpuPowerReader.Instance, reader);
        Assert.Equal(1, query.DisposeCalls);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(double.NaN)]
    [InlineData(EnergyMeterCpuPowerReader.MaximumReasonableMilliwatts + 1)]
    public void invalid_energy_meter_value_is_unavailable(double milliwatts)
    {
        const string package = @"\Energy Meter(RAPL_Package0_PKG)\Power";
        using var query = new FakePowerCounterQuery(new Dictionary<string, double>
        {
            [package] = milliwatts,
        });
        using var reader = EnergyMeterCpuPowerReader.CreateForTest(query, [package]);

        CpuPowerReading reading = reader.Sample();

        Assert.Equal(MetricStatus.Unavailable, reading.Status);
        Assert.True(double.IsNaN(reading.Watts));
    }

    [Fact]
    public void energy_meter_requires_every_package_reading()
    {
        const string package0 = @"\Energy Meter(RAPL_Package0_PKG)\Power";
        const string package1 = @"\Energy Meter(RAPL_Package1_PKG)\Power";
        using var query = new FakePowerCounterQuery(new Dictionary<string, double>
        {
            [package0] = 20_000,
            [package1] = 25_000,
        });
        query.FailedReads.Add(package1);
        using var reader = EnergyMeterCpuPowerReader.CreateForTest(query, [package0, package1]);

        CpuPowerReading reading = reader.Sample();

        Assert.Equal(MetricStatus.Unavailable, reading.Status);
    }

    [Fact]
    public void energy_meter_collect_failure_is_unavailable()
    {
        const string package = @"\Energy Meter(RAPL_Package0_PKG)\Power";
        using var query = new FakePowerCounterQuery(new Dictionary<string, double>
        {
            [package] = 20_000,
        })
        {
            CollectResult = false,
        };
        using var reader = EnergyMeterCpuPowerReader.CreateForTest(query, [package]);

        Assert.Equal(MetricStatus.Unavailable, reader.Sample().Status);
    }

    [Fact]
    public void partial_counter_initialization_disposes_owned_query()
    {
        const string package0 = @"\Energy Meter(RAPL_Package0_PKG)\Power";
        const string package1 = @"\Energy Meter(RAPL_Package1_PKG)\Power";
        var query = new FakePowerCounterQuery(new Dictionary<string, double>
        {
            [package0] = 20_000,
        });

        using ICpuPowerReader reader = EnergyMeterCpuPowerReader.TryCreateForTest(
            query,
            [package0, package1]);

        Assert.Same(UnavailableCpuPowerReader.Instance, reader);
        Assert.Equal(1, query.DisposeCalls);
    }

    [Fact]
    public void unavailable_cpu_reader_retries_initialization_after_deadline()
    {
        DateTimeOffset now = new(2026, 7, 12, 0, 0, 0, TimeSpan.Zero);
        int factoryCalls = 0;
        using var reader = new RetryingCpuPowerReader(
            () => ++factoryCalls == 1
                ? UnavailableCpuPowerReader.Instance
                : new FixedCpuPowerReader(31.5),
            TimeSpan.FromSeconds(30),
            () => now);

        Assert.Equal(MetricStatus.Unavailable, reader.Sample().Status);
        now = now.AddSeconds(29);
        Assert.Equal(MetricStatus.Unavailable, reader.Sample().Status);
        Assert.Equal(1, factoryCalls);

        now = now.AddSeconds(1);
        CpuPowerReading recovered = reader.Sample();

        Assert.Equal(2, factoryCalls);
        Assert.Equal(MetricStatus.Ok, recovered.Status);
        Assert.Equal(31.5, recovered.Watts);
    }

    [Fact]
    public void live_cpu_reader_is_recreated_after_being_unavailable_for_retry_interval()
    {
        DateTimeOffset now = new(2026, 7, 12, 0, 0, 0, TimeSpan.Zero);
        int factoryCalls = 0;
        var disconnected = new SequenceCpuPowerReader(CpuPowerReading.Unavailable());
        using var reader = new RetryingCpuPowerReader(
            () => ++factoryCalls == 1
                ? disconnected
                : new FixedCpuPowerReader(42),
            TimeSpan.FromSeconds(30),
            () => now);

        Assert.Equal(MetricStatus.Unavailable, reader.Sample().Status);
        now = now.AddSeconds(29);
        Assert.Equal(MetricStatus.Unavailable, reader.Sample().Status);
        Assert.Equal(1, factoryCalls);
        Assert.Equal(0, disconnected.DisposeCalls);

        now = now.AddSeconds(1);
        CpuPowerReading recovered = reader.Sample();

        Assert.Equal(2, factoryCalls);
        Assert.Equal(1, disconnected.DisposeCalls);
        Assert.Equal(MetricStatus.Ok, recovered.Status);
        Assert.Equal(42, recovered.Watts);
    }

    [Fact]
    public void successful_live_sample_cancels_pending_reader_replacement()
    {
        DateTimeOffset now = new(2026, 7, 12, 0, 0, 0, TimeSpan.Zero);
        int factoryCalls = 0;
        var transient = new SequenceCpuPowerReader(
            CpuPowerReading.Unavailable(),
            new CpuPowerReading(MetricStatus.Ok, 25));
        using var reader = new RetryingCpuPowerReader(
            () =>
            {
                factoryCalls++;
                return transient;
            },
            TimeSpan.FromSeconds(30),
            () => now);

        Assert.Equal(MetricStatus.Unavailable, reader.Sample().Status);
        now = now.AddSeconds(1);
        Assert.Equal(MetricStatus.Ok, reader.Sample().Status);
        now = now.AddMinutes(1);
        Assert.Equal(MetricStatus.Ok, reader.Sample().Status);

        Assert.Equal(1, factoryCalls);
        Assert.Equal(0, transient.DisposeCalls);
    }

    [Fact]
    public void native_cpu_power_overrides_hardware_value_and_unavailable_native_falls_back()
    {
        var hardware = new PowerSnapshot
        {
            GpuCollectionStatus = MetricStatus.Unavailable,
            CpuPackageStatus = MetricStatus.Ok,
            CpuPackageWatts = 15,
            GpuReadings = [],
        };

        PowerSnapshot preferred = PowerSnapshotCpuPreference.Apply(
            hardware,
            new CpuPowerReading(MetricStatus.Ok, 29.5));
        PowerSnapshot fallback = PowerSnapshotCpuPreference.Apply(
            hardware,
            CpuPowerReading.Unavailable());

        Assert.Equal(29.5, preferred.CpuPackageWatts);
        Assert.Equal(hardware, fallback);
    }

    [Theory]
    [InlineData(Architecture.Arm64, false)]
    [InlineData(Architecture.X64, true)]
    [InlineData(Architecture.X86, true)]
    public void libre_hardware_monitor_is_skipped_only_on_arm64_os(
        Architecture architecture,
        bool expected)
    {
        Assert.Equal(
            expected,
            LibreHardwarePowerSession.ShouldOpenLibreHardwareMonitor(architecture));
    }

    [Fact]
    public void arm64_session_keeps_native_cpu_power_without_opening_hardware_monitor()
    {
        using var session = new LibreHardwarePowerSession(
            onError: null,
            Architecture.Arm64,
            () => new FixedCpuPowerReader(31.5));

        PowerSnapshot snapshot = session.Sample();

        Assert.Equal(MetricStatus.Ok, snapshot.CpuPackageStatus);
        Assert.Equal(31.5, snapshot.CpuPackageWatts);
        Assert.Equal(MetricStatus.Unavailable, snapshot.GpuCollectionStatus);
        Assert.Empty(snapshot.GpuReadings);
    }

    [Fact]
    public void hardware_monitor_open_failure_closes_partial_initialization_and_rethrows()
    {
        var computer = new Computer();
        var openFailure = new InvalidOperationException("open failed");
        bool closeCalled = false;

        InvalidOperationException thrown = Assert.Throws<InvalidOperationException>(() =>
            LibreHardwarePowerSession.OpenWithCleanup(
                computer,
                _ => throw openFailure,
                _ => closeCalled = true,
                onError: null));

        Assert.Same(openFailure, thrown);
        Assert.True(closeCalled);
    }

    [Fact]
    public void hardware_monitor_cleanup_failure_is_reported_without_masking_open_failure()
    {
        var computer = new Computer();
        var openFailure = new InvalidOperationException("open failed");
        var cleanupFailure = new InvalidOperationException("cleanup failed");
        var reports = new List<(string Stage, Exception Exception)>();

        InvalidOperationException thrown = Assert.Throws<InvalidOperationException>(() =>
            LibreHardwarePowerSession.OpenWithCleanup(
                computer,
                _ => throw openFailure,
                _ => throw cleanupFailure,
                (stage, exception) => reports.Add((stage, exception))));

        Assert.Same(openFailure, thrown);
        Assert.Collection(
            reports,
            report =>
            {
                Assert.Equal("power-initialization-cleanup-failure", report.Stage);
                Assert.Same(cleanupFailure, report.Exception);
            });
    }

    [Fact]
    public async Task sampling_returns_cached_value_while_worker_poll_is_blocked()
    {
        var session = new BlockingSession();
        using var source = new HardwarePowerMetricSource(
            () => session,
            TimeSpan.FromSeconds(1),
            TimeSpan.FromSeconds(1));
        Assert.True(session.Started.Wait(TimeSpan.FromSeconds(2)));

        ValueTask<PowerSnapshot> pending = source.SampleAsync(default);

        Assert.True(pending.IsCompletedSuccessfully);
        Assert.Equal(MetricStatus.WarmingUp, (await pending).CpuPackageStatus);

        session.Release.Set();
        Assert.True(SpinWait.SpinUntil(
            () => source.SampleAsync(default).Result.CpuPackageStatus == MetricStatus.Ok,
            TimeSpan.FromSeconds(2)));
    }

    [Fact]
    public void pause_suppresses_polling_and_resume_requests_an_immediate_poll()
    {
        var session = new BlockingSession();
        using var source = new HardwarePowerMetricSource(
            () => session,
            TimeSpan.FromMilliseconds(20),
            TimeSpan.FromSeconds(1));
        Assert.True(session.Started.Wait(TimeSpan.FromSeconds(2)));

        source.PausePolling();
        session.Release.Set();
        Assert.True(SpinWait.SpinUntil(() => session.Calls == 1, TimeSpan.FromSeconds(2)));
        Thread.Sleep(100);
        Assert.Equal(1, session.Calls);

        source.ResumePolling();
        Assert.True(SpinWait.SpinUntil(() => session.Calls >= 2, TimeSpan.FromSeconds(2)));
    }

    [Fact]
    public void dispose_returns_after_deadline_and_session_is_released_when_poll_finishes()
    {
        var session = new BlockingSession();
        var source = new HardwarePowerMetricSource(
            () => session,
            TimeSpan.FromSeconds(1),
            TimeSpan.FromMilliseconds(20));
        Assert.True(session.Started.Wait(TimeSpan.FromSeconds(2)));

        source.Dispose();
        Assert.Equal(0, session.DisposeCalls);

        session.Release.Set();
        Assert.True(SpinWait.SpinUntil(() => session.DisposeCalls == 1, TimeSpan.FromSeconds(2)));
    }

    [Fact]
    public async Task pause_and_resume_are_safe_during_concurrent_dispose()
    {
        var source = new HardwarePowerMetricSource(
            () => new ImmediateSession(),
            TimeSpan.FromMilliseconds(1),
            TimeSpan.FromSeconds(1));
        var errors = new System.Collections.Concurrent.ConcurrentQueue<Exception>();
        Task[] toggles = Enumerable.Range(0, 4).Select(_ => Task.Run(() =>
        {
            for (int i = 0; i < 1_000; i++)
            {
                try
                {
                    source.PausePolling();
                    source.ResumePolling();
                }
                catch (Exception ex)
                {
                    errors.Enqueue(ex);
                }
            }
        })).ToArray();

        source.Dispose();
        await Task.WhenAll(toggles);

        Assert.Empty(errors);
    }

    private sealed class BlockingSession : IHardwarePowerSession
    {
        private int _calls;
        private int _disposeCalls;

        public ManualResetEventSlim Started { get; } = new();
        public ManualResetEventSlim Release { get; } = new();
        public int Calls => Volatile.Read(ref _calls);
        public int DisposeCalls => Volatile.Read(ref _disposeCalls);

        public PowerSnapshot Sample()
        {
            Interlocked.Increment(ref _calls);
            Started.Set();
            Release.Wait();
            return new PowerSnapshot
            {
                GpuCollectionStatus = MetricStatus.Unavailable,
                CpuPackageStatus = MetricStatus.Ok,
                CpuPackageWatts = 50,
                GpuReadings = [],
            };
        }

        public void Dispose()
        {
            Interlocked.Increment(ref _disposeCalls);
            Started.Dispose();
            Release.Dispose();
        }
    }

    private sealed class ImmediateSession : IHardwarePowerSession
    {
        public PowerSnapshot Sample() => PowerSnapshot.Unavailable();

        public void Dispose()
        {
        }
    }

    private sealed class FakePowerCounterQuery : IPowerCounterQuery
    {
        private readonly IReadOnlyDictionary<string, double> _values;

        public FakePowerCounterQuery(IReadOnlyDictionary<string, double> values)
        {
            _values = values;
        }

        public List<string> AddedPaths { get; } = [];
        public HashSet<string> FailedReads { get; } = new(StringComparer.Ordinal);
        public bool CollectResult { get; init; } = true;
        public int DisposeCalls { get; private set; }
        public bool TryAddCounter(string path)
        {
            AddedPaths.Add(path);
            return _values.ContainsKey(path);
        }

        public bool Collect() => CollectResult;

        public bool TryGetDouble(string path, out double value)
        {
            if (FailedReads.Contains(path))
            {
                value = double.NaN;
                return false;
            }
            return _values.TryGetValue(path, out value);
        }

        public void Dispose()
        {
            DisposeCalls++;
        }
    }

    private sealed class FixedCpuPowerReader(double watts) : ICpuPowerReader
    {
        public CpuPowerReading Sample() => new(MetricStatus.Ok, watts);
        public void Dispose()
        {
        }
    }

    private sealed class SequenceCpuPowerReader(params CpuPowerReading[] readings) : ICpuPowerReader
    {
        private int _index;

        public int DisposeCalls { get; private set; }

        public CpuPowerReading Sample()
        {
            int index = Math.Min(_index, readings.Length - 1);
            _index++;
            return readings[index];
        }

        public void Dispose() => DisposeCalls++;
    }
}
