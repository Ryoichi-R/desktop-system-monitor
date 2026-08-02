using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using DesktopSystemMonitor.Core.Formatting;
using DesktopSystemMonitor.Core.Metrics;
using DesktopSystemMonitor.Core.Settings;
using Xunit;

namespace DesktopSystemMonitor.App.Tests;

public sealed class MetricViewModelTests
{
    [Fact]
    public void memory_warmup_has_no_unit_or_bar()
    {
        var viewModel = new MetricViewModel();

        viewModel.Apply(CreateSnapshot(MemorySnapshot.Warmup()), RateUnitSystem.DecimalBytes);

        Assert.Equal("--", viewModel.MemoryValue);
        Assert.Equal(string.Empty, viewModel.MemoryUnit);
        Assert.Equal(0d, viewModel.MemoryBarFraction);
        Assert.Equal("N/A", viewModel.MemoryUsage);
    }

    [Fact]
    public void unavailable_memory_has_no_unit_or_bar()
    {
        var viewModel = new MetricViewModel();

        viewModel.Apply(CreateSnapshot(MemorySnapshot.Unavailable()), RateUnitSystem.DecimalBytes);

        Assert.Equal("N/A", viewModel.MemoryValue);
        Assert.Equal(string.Empty, viewModel.MemoryUnit);
        Assert.Equal(0d, viewModel.MemoryBarFraction);
        Assert.Equal("N/A", viewModel.MemoryUsage);
    }

    [Fact]
    public void available_memory_separates_percent_and_sets_bar()
    {
        var viewModel = new MetricViewModel();
        var memory = new MemorySnapshot
        {
            Status = MetricStatus.Ok,
            UtilizationPercent = 75,
            UsedBytes = 12L << 30,
            TotalBytes = 16L << 30,
        };

        viewModel.Apply(CreateSnapshot(memory), RateUnitSystem.DecimalBytes);

        Assert.Equal("75", viewModel.MemoryValue);
        Assert.Equal("%", viewModel.MemoryUnit);
        Assert.Equal(0.75, viewModel.MemoryBarFraction);
        Assert.Equal("12.0/16.0 GiB", viewModel.MemoryUsage);
    }

    [Fact]
    public void cpu_percent_is_clamped_for_value_and_bar()
    {
        var viewModel = new MetricViewModel();
        var cpu = new CpuSnapshot
        {
            UtilizationStatus = MetricStatus.Ok,
            UtilizationPercent = 125,
            FrequencyStatus = MetricStatus.Ok,
            FrequencyMhz = 3200,
            FrequencyIsEstimate = true,
            RawUtilizationPercent = 125,
        };

        viewModel.Apply(CreateSnapshot(MemorySnapshot.Warmup()) with { Cpu = cpu }, RateUnitSystem.DecimalBytes);

        Assert.Equal("100", viewModel.CpuValue);
        Assert.Equal("%", viewModel.CpuUnit);
        Assert.Equal(1d, viewModel.CpuBarFraction);
        Assert.Equal("3.20 GHz*", viewModel.CpuFrequency);
    }

    [Theory]
    [InlineData(MetricStatus.WarmingUp, "--")]
    [InlineData(MetricStatus.Unavailable, "N/A")]
    public void cpu_missing_states_have_no_unit_or_bar(MetricStatus status, string expectedValue)
    {
        var viewModel = new MetricViewModel();
        CpuSnapshot cpu = status == MetricStatus.WarmingUp ? CpuSnapshot.Warmup() : CpuSnapshot.Unavailable();

        viewModel.Apply(CreateSnapshot(MemorySnapshot.Warmup()) with { Cpu = cpu }, RateUnitSystem.DecimalBytes);

        Assert.Equal(expectedValue, viewModel.CpuValue);
        Assert.Equal(string.Empty, viewModel.CpuUnit);
        Assert.Equal(0d, viewModel.CpuBarFraction);
    }

    [Fact]
    public void gpu_available_and_unavailable_states_update_all_display_parts()
    {
        var viewModel = new MetricViewModel();
        var adapter = new GpuAdapterSnapshot
        {
            Luid = 1,
            DisplayName = "GPU",
            UtilizationStatus = MetricStatus.Ok,
            UtilizationPercent = 40,
            BusiestEngineType = "3D",
            MemoryStatus = MetricStatus.Ok,
            DedicatedUsageBytes = 2L << 30,
            DedicatedLimitBytes = 8L << 30,
            IsIntegrated = false,
        };
        var gpu = new GpuSnapshot
        {
            Adapters = ImmutableArray.Create(adapter),
            OverallStatus = MetricStatus.Ok,
        };

        viewModel.Apply(CreateSnapshot(MemorySnapshot.Warmup()) with { Gpu = gpu }, RateUnitSystem.DecimalBytes);

        Assert.Equal("40", viewModel.GpuValue);
        Assert.Equal("%", viewModel.GpuUnit);
        Assert.Equal(0.4, viewModel.GpuBarFraction);
        Assert.Equal("2.00/8.00 GiB", viewModel.GpuMemory);

        viewModel.Apply(CreateSnapshot(MemorySnapshot.Warmup()) with { Gpu = GpuSnapshot.Unavailable() }, RateUnitSystem.DecimalBytes);

        Assert.Equal("N/A", viewModel.GpuValue);
        Assert.Equal(string.Empty, viewModel.GpuUnit);
        Assert.Equal(0d, viewModel.GpuBarFraction);
        Assert.Equal("N/A", viewModel.GpuMemory);
    }

    [Fact]
    public void gpu_warmup_has_no_unit_or_bar()
    {
        var viewModel = new MetricViewModel();

        viewModel.Apply(CreateSnapshot(MemorySnapshot.Warmup()) with { Gpu = GpuSnapshot.Warmup() }, RateUnitSystem.DecimalBytes);

        Assert.Equal("--", viewModel.GpuValue);
        Assert.Equal(string.Empty, viewModel.GpuUnit);
        Assert.Equal(0d, viewModel.GpuBarFraction);
        Assert.Equal("N/A", viewModel.GpuMemory);
    }

    [Fact]
    public void apply_notifies_each_changed_memory_property()
    {
        var viewModel = new MetricViewModel();
        var changed = new List<string>();
        viewModel.PropertyChanged += (_, args) => changed.Add(args.PropertyName ?? string.Empty);
        var memory = new MemorySnapshot
        {
            Status = MetricStatus.Ok,
            UtilizationPercent = 50,
            UsedBytes = 4L << 30,
            TotalBytes = 8L << 30,
        };

        viewModel.Apply(CreateSnapshot(memory), RateUnitSystem.DecimalBytes);

        Assert.Contains(nameof(MetricViewModel.MemoryValue), changed);
        Assert.Contains(nameof(MetricViewModel.MemoryUnit), changed);
        Assert.Contains(nameof(MetricViewModel.MemoryBarFraction), changed);
        Assert.Contains(nameof(MetricViewModel.MemoryUsage), changed);
    }

    [Fact]
    public void apply_notifies_cpu_and_gpu_display_parts()
    {
        var viewModel = new MetricViewModel();
        var changed = new List<string>();
        viewModel.PropertyChanged += (_, args) => changed.Add(args.PropertyName ?? string.Empty);
        var cpu = new CpuSnapshot
        {
            UtilizationStatus = MetricStatus.Ok,
            UtilizationPercent = 25,
            FrequencyStatus = MetricStatus.WarmingUp,
            FrequencyMhz = double.NaN,
            FrequencyIsEstimate = true,
            RawUtilizationPercent = 25,
        };
        var gpu = new GpuSnapshot
        {
            OverallStatus = MetricStatus.Ok,
            Adapters = ImmutableArray.Create(new GpuAdapterSnapshot
            {
                Luid = 2,
                DisplayName = "GPU",
                UtilizationStatus = MetricStatus.Ok,
                UtilizationPercent = 60,
                BusiestEngineType = "3D",
                MemoryStatus = MetricStatus.Unavailable,
                DedicatedUsageBytes = -1,
                DedicatedLimitBytes = -1,
                IsIntegrated = false,
            }),
        };

        viewModel.Apply(CreateSnapshot(MemorySnapshot.Warmup()) with { Cpu = cpu, Gpu = gpu }, RateUnitSystem.DecimalBytes);

        Assert.Contains(nameof(MetricViewModel.CpuValue), changed);
        Assert.Contains(nameof(MetricViewModel.CpuUnit), changed);
        Assert.Contains(nameof(MetricViewModel.CpuBarFraction), changed);
        Assert.Contains(nameof(MetricViewModel.GpuValue), changed);
        Assert.Contains(nameof(MetricViewModel.GpuUnit), changed);
        Assert.Contains(nameof(MetricViewModel.GpuBarFraction), changed);
    }

    [Fact]
    public void network_can_use_fixed_kilobits_per_second()
    {
        var viewModel = new MetricViewModel();
        var network = new NetworkSnapshot
        {
            Adapters = [],
            AggregateStatus = MetricStatus.Ok,
            AggregateBytesReceivedPerSecond = 125_000,
            AggregateBytesSentPerSecond = 1_250,
        };

        viewModel.Apply(
            CreateSnapshot(MemorySnapshot.Warmup()) with { Network = network },
            RateUnitSystem.FixedKilobitsPerSecond);

        Assert.Equal("1000 Kb/s", viewModel.NetworkRx);
        Assert.Equal("10.0 Kb/s", viewModel.NetworkTx);
    }

    [Fact]
    public void power_values_are_formatted_and_gpu_is_matched_by_name()
    {
        var viewModel = new MetricViewModel();
        var gpu = new GpuSnapshot
        {
            OverallStatus = MetricStatus.Ok,
            Adapters = ImmutableArray.Create(new GpuAdapterSnapshot
            {
                Luid = 2,
                DisplayName = "NVIDIA GeForce RTX 4070",
                UtilizationStatus = MetricStatus.Ok,
                UtilizationPercent = 60,
                BusiestEngineType = "3D",
                MemoryStatus = MetricStatus.Unavailable,
                DedicatedUsageBytes = -1,
                DedicatedLimitBytes = -1,
                IsIntegrated = false,
            }),
        };
        var power = new PowerSnapshot
        {
            GpuCollectionStatus = MetricStatus.Ok,
            CpuPackageStatus = MetricStatus.Ok,
            CpuPackageWatts = 42.34,
            GpuReadings = ImmutableArray.Create(
                new GpuPowerReading { DisplayName = "AMD Radeon", Status = MetricStatus.Ok, Watts = 30 },
                new GpuPowerReading { DisplayName = "NVIDIA GeForce RTX 4070", Status = MetricStatus.Ok, Watts = 95.06 }),
        };

        viewModel.Apply(
            CreateSnapshot(MemorySnapshot.Warmup()) with { Gpu = gpu, Power = power },
            RateUnitSystem.DecimalBytes);

        Assert.Equal("42.3 W", viewModel.CpuPower);
        Assert.Equal("95.1 W", viewModel.GpuPower);
    }

    [Fact]
    public void unmatched_gpu_power_is_not_guessed_when_multiple_gpus_exist()
    {
        var viewModel = new MetricViewModel();
        var gpu = new GpuSnapshot
        {
            OverallStatus = MetricStatus.Ok,
            Adapters = ImmutableArray.Create(new GpuAdapterSnapshot
            {
                Luid = 3,
                DisplayName = "Same GPU",
                UtilizationStatus = MetricStatus.Ok,
                UtilizationPercent = 10,
                BusiestEngineType = "3D",
                MemoryStatus = MetricStatus.Unavailable,
                DedicatedUsageBytes = -1,
                DedicatedLimitBytes = -1,
                IsIntegrated = false,
            }),
        };
        var power = new PowerSnapshot
        {
            GpuCollectionStatus = MetricStatus.Ok,
            CpuPackageStatus = MetricStatus.Unavailable,
            CpuPackageWatts = double.NaN,
            GpuReadings = ImmutableArray.Create(
                new GpuPowerReading { DisplayName = "Same GPU", Status = MetricStatus.Ok, Watts = 10 },
                new GpuPowerReading { DisplayName = "Same GPU", Status = MetricStatus.Ok, Watts = 20 }),
        };

        viewModel.Apply(
            CreateSnapshot(MemorySnapshot.Warmup()) with { Gpu = gpu, Power = power },
            RateUnitSystem.DecimalBytes);

        Assert.Equal("N/A", viewModel.CpuPower);
        Assert.Equal("N/A", viewModel.GpuPower);
    }

    [Fact]
    public void gpu_power_warmup_uses_gpu_collection_status_not_cpu_status()
    {
        var viewModel = new MetricViewModel();
        var power = new PowerSnapshot
        {
            GpuCollectionStatus = MetricStatus.WarmingUp,
            CpuPackageStatus = MetricStatus.Ok,
            CpuPackageWatts = 25,
            GpuReadings = [],
        };

        viewModel.Apply(CreateSnapshot(MemorySnapshot.Warmup()) with { Power = power }, RateUnitSystem.DecimalBytes);

        Assert.Equal("25.0 W", viewModel.CpuPower);
        Assert.Equal("--", viewModel.GpuPower);
    }

    [Fact]
    public void battery_estimator_accumulates_when_recent_peaks_are_disabled()
    {
        var viewModel = new MetricViewModel();
        var settings = new AppSettings { ShowRecentPeaks = false };
        DateTimeOffset start = DateTimeOffset.UtcNow;
        var battery = new BatterySnapshot
        {
            Status = MetricStatus.Ok,
            PowerState = BatteryPowerState.Discharging,
            BatteryPresent = true,
            Percent = 63,
            RemainingCapacityMilliwattHours = 32_000,
            RateMilliwatts = -8_000,
            WindowsEstimatedTime = null,
        };

        for (int index = 0; index < 5; index++)
        {
            viewModel.Apply(
                CreateSnapshot(MemorySnapshot.Warmup()) with
                {
                    TakenAt = start.AddSeconds(index),
                    Battery = battery,
                },
                RateUnitSystem.DecimalBytes,
                settings);
        }

        Assert.Equal("≈4h 0m", viewModel.BatteryPrimary);
        Assert.Equal("63% · 8.0 W", viewModel.BatterySecondary);
    }

    [Fact]
    public void charging_estimator_displays_time_target_and_charge_power()
    {
        var viewModel = new MetricViewModel();
        var settings = new AppSettings { BatteryChargeTargetPercent = 80 }.Normalized();
        DateTimeOffset start = DateTimeOffset.UtcNow;
        var battery = new BatterySnapshot
        {
            Status = MetricStatus.Ok,
            PowerState = BatteryPowerState.Charging,
            BatteryPresent = true,
            Percent = 50,
            RemainingCapacityMilliwattHours = 30_000,
            RateMilliwatts = 12_000,
            WindowsEstimatedTime = TimeSpan.FromMinutes(1),
        };

        for (int index = 0; index < 5; index++)
        {
            viewModel.Apply(
                CreateSnapshot(MemorySnapshot.Warmup()) with
                {
                    TakenAt = start.AddSeconds(index),
                    Battery = battery,
                },
                RateUnitSystem.DecimalBytes,
                settings);
        }

        Assert.Equal("≈1h 30m", viewModel.BatteryPrimary);
        Assert.Contains("50%", viewModel.BatterySecondary, StringComparison.Ordinal);
        Assert.Contains("80%", viewModel.BatterySecondary, StringComparison.Ordinal);
        Assert.Contains("12.0", viewModel.BatterySecondary, StringComparison.Ordinal);
        Assert.DoesNotContain("1m", viewModel.BatteryPrimary, StringComparison.Ordinal);
        Assert.True(BatteryDisplayFormatter.Fits(viewModel.BatterySecondary, settings.FontFamilyName));
    }

    [Fact]
    public void charging_warmup_and_unavailable_rate_do_not_show_false_times()
    {
        var viewModel = new MetricViewModel();
        var settings = new AppSettings { LearnedBatteryChargeTargetPercent = 80 }.Normalized();
        MetricSnapshot snapshot = CreateSnapshot(MemorySnapshot.Warmup()) with
        {
            Battery = new BatterySnapshot
            {
                Status = MetricStatus.Ok,
                PowerState = BatteryPowerState.Charging,
                BatteryPresent = true,
                Percent = 62,
                RemainingCapacityMilliwattHours = 30_000,
                RateMilliwatts = 12_000,
                WindowsEstimatedTime = null,
            },
        };

        viewModel.Apply(snapshot, RateUnitSystem.DecimalBytes, settings);
        Assert.Equal("充電中", viewModel.BatteryPrimary);
        Assert.Equal("62%→80%", viewModel.BatterySecondary);

        viewModel.Apply(
            snapshot with { TakenAt = snapshot.TakenAt.AddSeconds(1), Battery = snapshot.Battery with { RateMilliwatts = 0 } },
            RateUnitSystem.DecimalBytes,
            settings);
        Assert.Equal("充電中", viewModel.BatteryPrimary);
        Assert.Equal("62%→80% · N/A", viewModel.BatterySecondary);
    }

    [Theory]
    [InlineData(4, "4%→80%")]
    [InlineData(79.6, "80% · 目標80%")]
    [InlineData(80, "80% · 目標80%")]
    [InlineData(90, "90% · 目標80%")]
    public void charging_boundary_displays_avoid_misleading_unavailable_or_direction(
        double percent,
        string expectedSecondary)
    {
        var viewModel = new MetricViewModel();
        var settings = new AppSettings { BatteryChargeTargetPercent = 80 }.Normalized();
        MetricSnapshot snapshot = CreateSnapshot(MemorySnapshot.Warmup()) with
        {
            Battery = new BatterySnapshot
            {
                Status = MetricStatus.Ok,
                PowerState = BatteryPowerState.Charging,
                BatteryPresent = true,
                Percent = percent,
                RemainingCapacityMilliwattHours = 30_000,
                RateMilliwatts = 12_000,
                WindowsEstimatedTime = null,
            },
        };

        viewModel.Apply(snapshot, RateUnitSystem.DecimalBytes, settings);

        Assert.Equal("充電中", viewModel.BatteryPrimary);
        Assert.Equal(expectedSecondary, viewModel.BatterySecondary);
    }

    [Theory]
    [InlineData(100, null, "上限到達", "未学習")]
    [InlineData(100, 100, "上限到達", "手動")]
    [InlineData(80, null, "上限到達", "自動")]
    public void reached_target_displays_the_target_source(
        double percent,
        int? manualTarget,
        string expectedPrimary,
        string expectedSource)
    {
        var viewModel = new MetricViewModel();
        var settings = new AppSettings
        {
            BatteryChargeTargetPercent = manualTarget,
            LearnedBatteryChargeTargetPercent = percent == 80 ? 80 : null,
        }.Normalized();
        var battery = new BatterySnapshot
        {
            Status = MetricStatus.Ok,
            PowerState = BatteryPowerState.AcConnected,
            BatteryPresent = true,
            Percent = percent,
            RemainingCapacityMilliwattHours = 30_000,
            RateMilliwatts = 0,
            WindowsEstimatedTime = null,
        };

        viewModel.Apply(
            CreateSnapshot(MemorySnapshot.Warmup()) with { Battery = battery },
            RateUnitSystem.DecimalBytes,
            settings);

        Assert.Equal(expectedPrimary, viewModel.BatteryPrimary);
        Assert.Contains(expectedSource, viewModel.BatterySecondary, StringComparison.Ordinal);
    }

    [Fact]
    public void reset_transient_state_clears_charge_rate_samples()
    {
        var viewModel = new MetricViewModel();
        var settings = new AppSettings { BatteryChargeTargetPercent = 80 }.Normalized();
        DateTimeOffset start = DateTimeOffset.UtcNow;
        var battery = new BatterySnapshot
        {
            Status = MetricStatus.Ok,
            PowerState = BatteryPowerState.Charging,
            BatteryPresent = true,
            Percent = 50,
            RemainingCapacityMilliwattHours = 30_000,
            RateMilliwatts = 12_000,
            WindowsEstimatedTime = null,
        };
        for (int index = 0; index < 5; index++)
        {
            viewModel.Apply(
                CreateSnapshot(MemorySnapshot.Warmup()) with { TakenAt = start.AddSeconds(index), Battery = battery },
                RateUnitSystem.DecimalBytes,
                settings);
        }

        viewModel.ResetTransientState();
        viewModel.Apply(
            CreateSnapshot(MemorySnapshot.Warmup()) with { TakenAt = start.AddSeconds(5), Battery = battery },
            RateUnitSystem.DecimalBytes,
            settings);

        Assert.Equal("充電中", viewModel.BatteryPrimary);
        Assert.Equal("50%→80%", viewModel.BatterySecondary);
    }

    [Fact]
    public void gpu_temperature_is_not_guessed_from_a_non_matching_single_reading()
    {
        var viewModel = new MetricViewModel();
        var gpu = new GpuSnapshot
        {
            OverallStatus = MetricStatus.Ok,
            Adapters = ImmutableArray.Create(new GpuAdapterSnapshot
            {
                Luid = 4,
                DisplayName = "Intel Arc Graphics",
                UtilizationStatus = MetricStatus.Ok,
                UtilizationPercent = 10,
                BusiestEngineType = "3D",
                MemoryStatus = MetricStatus.Unavailable,
                DedicatedUsageBytes = -1,
                DedicatedLimitBytes = -1,
                IsIntegrated = true,
            }),
        };
        var temperature = new TemperatureSnapshot
        {
            CpuPackageStatus = MetricStatus.Unavailable,
            CpuPackageCelsius = double.NaN,
            GpuCollectionStatus = MetricStatus.Ok,
            GpuReadings = ImmutableArray.Create(new GpuTemperatureReading
            {
                DisplayName = "NVIDIA GeForce RTX 4070",
                Status = MetricStatus.Ok,
                Celsius = 55,
            }),
        };

        viewModel.Apply(
            CreateSnapshot(MemorySnapshot.Warmup()) with { Gpu = gpu, Temperature = temperature },
            RateUnitSystem.DecimalBytes);

        Assert.Equal("N/A", viewModel.GpuTemperature);
    }

    [Fact]
    public void gpu_temperature_uses_a_unique_name_match()
    {
        var viewModel = new MetricViewModel();
        var gpu = new GpuSnapshot
        {
            OverallStatus = MetricStatus.Ok,
            Adapters = ImmutableArray.Create(new GpuAdapterSnapshot
            {
                Luid = 5,
                DisplayName = "NVIDIA GeForce RTX 4070",
                UtilizationStatus = MetricStatus.Ok,
                UtilizationPercent = 10,
                BusiestEngineType = "3D",
                MemoryStatus = MetricStatus.Unavailable,
                DedicatedUsageBytes = -1,
                DedicatedLimitBytes = -1,
                IsIntegrated = false,
            }),
        };
        var temperature = new TemperatureSnapshot
        {
            CpuPackageStatus = MetricStatus.Unavailable,
            CpuPackageCelsius = double.NaN,
            GpuCollectionStatus = MetricStatus.Ok,
            GpuReadings = ImmutableArray.Create(new GpuTemperatureReading
            {
                DisplayName = "NVIDIA GeForce RTX 4070",
                Status = MetricStatus.Ok,
                Celsius = 55,
            }),
        };

        viewModel.Apply(
            CreateSnapshot(MemorySnapshot.Warmup()) with { Gpu = gpu, Temperature = temperature },
            RateUnitSystem.DecimalBytes);

        Assert.Equal("55°C", viewModel.GpuTemperature);
    }

    [Fact]
    public void re_enabling_cpu_and_gpu_clears_bound_peak_offsets_immediately()
    {
        var viewModel = new MetricViewModel
        {
            CpuPeakOffset = 180,
            GpuPeakOffset = 90,
            MemoryPeakOffset = 45,
            DiskPeakOffset = 30,
        };

        viewModel.ApplyMetricVisibility(false, false);
        viewModel.ApplyMetricVisibility(true, true);

        Assert.Equal(0, viewModel.CpuPeakOffset);
        Assert.Equal(0, viewModel.GpuPeakOffset);
        Assert.Equal(45, viewModel.MemoryPeakOffset);
        Assert.Equal(30, viewModel.DiskPeakOffset);
    }

    [Fact]
    public void network_peaks_can_be_enabled_without_metric_peak_markers()
    {
        var viewModel = new MetricViewModel();
        var network = new NetworkSnapshot
        {
            Adapters = [],
            AggregateStatus = MetricStatus.Ok,
            AggregateBytesReceivedPerSecond = 100_000,
            AggregateBytesSentPerSecond = 50_000,
        };
        var settings = new AppSettings
        {
            ShowRecentPeaks = false,
            ShowNetworkPeaks = true,
        };

        viewModel.Apply(
            CreateSnapshot(MemorySnapshot.Warmup()) with { Network = network },
            RateUnitSystem.DecimalBytes,
            settings);

        Assert.Equal(0, viewModel.CpuPeakOffset);
        Assert.Equal(BytesPerSecondFormatter.Format(100_000, RateUnitSystem.DecimalBytes), viewModel.NetworkPeakRx);
        Assert.Equal(BytesPerSecondFormatter.Format(50_000, RateUnitSystem.DecimalBytes), viewModel.NetworkPeakTx);
    }

    [Fact]
    public void metric_peak_markers_can_be_enabled_without_network_peaks()
    {
        var viewModel = new MetricViewModel();
        var cpu = new CpuSnapshot
        {
            UtilizationStatus = MetricStatus.Ok,
            UtilizationPercent = 80,
            FrequencyStatus = MetricStatus.Unavailable,
            FrequencyMhz = 0,
            FrequencyIsEstimate = false,
            RawUtilizationPercent = 80,
        };
        var network = new NetworkSnapshot
        {
            Adapters = [],
            AggregateStatus = MetricStatus.Ok,
            AggregateBytesReceivedPerSecond = 100_000,
            AggregateBytesSentPerSecond = 50_000,
        };
        var settings = new AppSettings
        {
            ShowRecentPeaks = true,
            ShowNetworkPeaks = false,
        };

        viewModel.Apply(
            CreateSnapshot(MemorySnapshot.Warmup()) with { Cpu = cpu, Network = network },
            RateUnitSystem.DecimalBytes,
            settings);

        Assert.Equal(180, viewModel.CpuPeakOffset);
        Assert.Equal(BytesPerSecondFormatter.WarmingUp, viewModel.NetworkPeakRx);
        Assert.Equal(BytesPerSecondFormatter.WarmingUp, viewModel.NetworkPeakTx);
    }

    [Fact]
    public void changing_network_peak_window_updates_label_and_discards_old_samples()
    {
        var viewModel = new MetricViewModel();
        var settings = new AppSettings { ShowNetworkPeaks = true };
        DateTimeOffset start = DateTimeOffset.UtcNow;
        var high = new NetworkSnapshot
        {
            Adapters = [],
            AggregateStatus = MetricStatus.Ok,
            AggregateBytesReceivedPerSecond = 100_000,
            AggregateBytesSentPerSecond = 50_000,
        };

        viewModel.Apply(CreateSnapshot(MemorySnapshot.Warmup()) with { TakenAt = start, Network = high }, RateUnitSystem.DecimalBytes, settings);
        Assert.Equal(BytesPerSecondFormatter.Format(100_000, RateUnitSystem.DecimalBytes), viewModel.NetworkPeakRx);

        viewModel.ApplyNetworkPeakWindow(10);

        Assert.Equal("10s max", viewModel.NetworkPeakLabel);
        Assert.Equal(BytesPerSecondFormatter.WarmingUp, viewModel.NetworkPeakRx);
        Assert.Equal(BytesPerSecondFormatter.WarmingUp, viewModel.NetworkPeakTx);
    }

    [Theory]
    [InlineData(10)]
    [InlineData(30)]
    [InlineData(60)]
    public void network_peak_uses_the_configured_time_window(int seconds)
    {
        var viewModel = new MetricViewModel();
        var settings = new AppSettings { ShowNetworkPeaks = true };
        DateTimeOffset start = DateTimeOffset.UtcNow;
        var high = new NetworkSnapshot
        {
            Adapters = [],
            AggregateStatus = MetricStatus.Ok,
            AggregateBytesReceivedPerSecond = 100_000,
            AggregateBytesSentPerSecond = 50_000,
        };
        var low = high with
        {
            AggregateBytesReceivedPerSecond = 1_000,
            AggregateBytesSentPerSecond = 500,
        };
        viewModel.ApplyNetworkPeakWindow(seconds);

        viewModel.Apply(CreateSnapshot(MemorySnapshot.Warmup()) with { TakenAt = start, Network = high }, RateUnitSystem.DecimalBytes, settings);
        viewModel.Apply(CreateSnapshot(MemorySnapshot.Warmup()) with { TakenAt = start.AddSeconds(seconds - 0.001), Network = low }, RateUnitSystem.DecimalBytes, settings);
        Assert.Equal(BytesPerSecondFormatter.Format(100_000, RateUnitSystem.DecimalBytes), viewModel.NetworkPeakRx);

        viewModel.Apply(CreateSnapshot(MemorySnapshot.Warmup()) with { TakenAt = start.AddSeconds(seconds + 0.001), Network = low }, RateUnitSystem.DecimalBytes, settings);

        Assert.Equal($"{seconds}s max", viewModel.NetworkPeakLabel);
        Assert.Equal(BytesPerSecondFormatter.Format(1_000, RateUnitSystem.DecimalBytes), viewModel.NetworkPeakRx);
        Assert.Equal(BytesPerSecondFormatter.Format(500, RateUnitSystem.DecimalBytes), viewModel.NetworkPeakTx);
    }

    private static MetricSnapshot CreateSnapshot(MemorySnapshot memory) => new()
    {
        TakenAt = DateTimeOffset.UtcNow,
        Cpu = CpuSnapshot.Warmup(),
        Memory = memory,
        Gpu = GpuSnapshot.Warmup(),
        Network = NetworkSnapshot.Warmup(),
    };
}
