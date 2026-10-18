using System.ComponentModel;
using System.Runtime.CompilerServices;
using DesktopSystemMonitor.Core.Formatting;
using DesktopSystemMonitor.Core.Metrics;
using DesktopSystemMonitor.Core.Sampling;
using DesktopSystemMonitor.Core.Settings;

namespace DesktopSystemMonitor.App;

public sealed class MetricViewModel : INotifyPropertyChanged
{
    private string _cpuValue = PercentFormatter.WarmingUp;
    private string _cpuUnit = string.Empty;
    private double _cpuBarFraction;
    private string _cpuFrequency = FrequencyFormatter.WarmingUp;
    private string _cpuPower = PowerFormatter.WarmingUp;
    private string _memoryValue = PercentFormatter.WarmingUp;
    private string _memoryUnit = string.Empty;
    private double _memoryBarFraction;
    private string _memoryUsage = BytesFormatter.Unavailable;
    private string _gpuValue = PercentFormatter.WarmingUp;
    private string _gpuUnit = string.Empty;
    private double _gpuBarFraction;
    private string _gpuMemory = BytesFormatter.Unavailable;
    private string _gpuPower = PowerFormatter.WarmingUp;
    private string _networkRx = BytesPerSecondFormatter.WarmingUp;
    private string _networkTx = BytesPerSecondFormatter.WarmingUp;
    private string _cpuTemperature = TemperatureFormatter.WarmingUp;
    private string _gpuTemperature = TemperatureFormatter.WarmingUp;
    private string _diskValue = PercentFormatter.WarmingUp;
    private string _diskRead = BytesPerSecondFormatter.WarmingUp;
    private string _diskWrite = BytesPerSecondFormatter.WarmingUp;
    private double _diskBarFraction;
    private string _batteryPrimary = "--";
    private string _batterySecondary = "--";
    private string _batteryFlowMarker = string.Empty;
    private string _networkPeakRx = BytesPerSecondFormatter.WarmingUp;
    private string _networkPeakTx = BytesPerSecondFormatter.WarmingUp;
    private string _networkPeakLabel = "60s max";
    private string _diskStatusMessage = string.Empty;
    private double _cpuPeakOffset;
    private double _memoryPeakOffset;
    private double _gpuPeakOffset;
    private double _diskPeakOffset;
    private readonly RecentPeakTracker _cpuPeak = new();
    private readonly RecentPeakTracker _memoryPeak = new();
    private readonly RecentPeakTracker _gpuPeak = new();
    private readonly RecentPeakTracker _diskPeak = new();
    private RecentPeakTracker _networkRxPeak = new();
    private RecentPeakTracker _networkTxPeak = new();
    private int _networkPeakWindowSeconds = 60;
    private bool _showCpuMetrics = true;
    private bool _showGpuMetrics = true;
    private readonly BatteryRuntimeEstimator _batteryEstimator = new();
    private readonly BatteryChargeTimeEstimator _batteryChargeEstimator = new();

    public event PropertyChangedEventHandler? PropertyChanged;

    public string CpuValue
    {
        get => _cpuValue;
        set => Set(ref _cpuValue, value);
    }
    public string CpuUnit
    {
        get => _cpuUnit;
        set => Set(ref _cpuUnit, value);
    }
    public double CpuBarFraction
    {
        get => _cpuBarFraction;
        set => Set(ref _cpuBarFraction, value);
    }
    public string CpuFrequency
    {
        get => _cpuFrequency;
        set => Set(ref _cpuFrequency, value);
    }
    public string CpuPower
    {
        get => _cpuPower;
        set => Set(ref _cpuPower, value);
    }
    public string MemoryValue
    {
        get => _memoryValue;
        set => Set(ref _memoryValue, value);
    }
    public string MemoryUnit
    {
        get => _memoryUnit;
        set => Set(ref _memoryUnit, value);
    }
    public double MemoryBarFraction
    {
        get => _memoryBarFraction;
        set => Set(ref _memoryBarFraction, value);
    }
    public string MemoryUsage
    {
        get => _memoryUsage;
        set => Set(ref _memoryUsage, value);
    }
    public string GpuValue
    {
        get => _gpuValue;
        set => Set(ref _gpuValue, value);
    }
    public string GpuUnit
    {
        get => _gpuUnit;
        set => Set(ref _gpuUnit, value);
    }
    public double GpuBarFraction
    {
        get => _gpuBarFraction;
        set => Set(ref _gpuBarFraction, value);
    }
    public string GpuMemory
    {
        get => _gpuMemory;
        set => Set(ref _gpuMemory, value);
    }
    public string GpuPower
    {
        get => _gpuPower;
        set => Set(ref _gpuPower, value);
    }
    public string NetworkRx
    {
        get => _networkRx;
        set => Set(ref _networkRx, value);
    }
    public string NetworkTx
    {
        get => _networkTx;
        set => Set(ref _networkTx, value);
    }
    public string CpuTemperature { get => _cpuTemperature; set => Set(ref _cpuTemperature, value); }
    public string GpuTemperature { get => _gpuTemperature; set => Set(ref _gpuTemperature, value); }
    public string DiskValue { get => _diskValue; set => Set(ref _diskValue, value); }
    public string DiskRead { get => _diskRead; set => Set(ref _diskRead, value); }
    public string DiskWrite { get => _diskWrite; set => Set(ref _diskWrite, value); }
    public double DiskBarFraction { get => _diskBarFraction; set => Set(ref _diskBarFraction, value); }
    public string BatteryPrimary { get => _batteryPrimary; set => Set(ref _batteryPrimary, value); }
    public string BatterySecondary { get => _batterySecondary; set => Set(ref _batterySecondary, value); }
    public string BatteryFlowMarker { get => _batteryFlowMarker; set => Set(ref _batteryFlowMarker, value); }
    public string NetworkPeakRx { get => _networkPeakRx; set => Set(ref _networkPeakRx, value); }
    public string NetworkPeakTx { get => _networkPeakTx; set => Set(ref _networkPeakTx, value); }
    public string NetworkPeakLabel { get => _networkPeakLabel; set => Set(ref _networkPeakLabel, value); }
    public string DiskStatusMessage { get => _diskStatusMessage; set => Set(ref _diskStatusMessage, value); }
    public double CpuPeakOffset { get => _cpuPeakOffset; set => Set(ref _cpuPeakOffset, value); }
    public double MemoryPeakOffset { get => _memoryPeakOffset; set => Set(ref _memoryPeakOffset, value); }
    public double GpuPeakOffset { get => _gpuPeakOffset; set => Set(ref _gpuPeakOffset, value); }
    public double DiskPeakOffset { get => _diskPeakOffset; set => Set(ref _diskPeakOffset, value); }

    public void Apply(MetricSnapshot snapshot, RateUnitSystem networkUnits)
        => Apply(snapshot, networkUnits, new AppSettings());

    public void Apply(MetricSnapshot snapshot, RateUnitSystem networkUnits, AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        (CpuValue, CpuUnit, CpuBarFraction) = FormatCpuUtilization(snapshot.Cpu);
        CpuFrequency = FormatCpuFrequency(snapshot.Cpu);
        CpuPower = FormatPower(snapshot.Power.CpuPackageStatus, snapshot.Power.CpuPackageWatts);
        (MemoryValue, MemoryUnit, MemoryBarFraction, MemoryUsage) = FormatMemory(snapshot.Memory);
        (GpuValue, GpuUnit, GpuBarFraction, GpuMemory) = FormatGpu(snapshot.Gpu);
        GpuPower = FormatGpuPower(snapshot.Gpu, snapshot.Power);
        (NetworkRx, NetworkTx) = FormatNetwork(snapshot.Network, networkUnits);
        CpuTemperature = FormatTemperature(snapshot.Temperature.CpuPackageStatus, snapshot.Temperature.CpuPackageCelsius);
        GpuTemperature = FormatGpuTemperature(snapshot.Gpu, snapshot.Temperature);
        ApplyDisk(snapshot.Disk, networkUnits);
        ApplyBattery(snapshot, settings);
        ApplyPeaks(snapshot, networkUnits, settings.ShowRecentPeaks, settings.EffectiveShowNetworkPeaks);
    }

    public void ResetTransientState()
    {
        ResetPeakState();
        _batteryEstimator.Clear();
        _batteryChargeEstimator.Clear();
    }

    internal void ResetBatteryChargeEstimate() => _batteryChargeEstimator.Clear();

    public void ApplyMetricVisibility(bool showCpu, bool showGpu)
    {
        if (!_showCpuMetrics && showCpu)
        {
            _cpuPeak.Clear();
            CpuPeakOffset = 0;
        }
        if (!_showGpuMetrics && showGpu)
        {
            _gpuPeak.Clear();
            GpuPeakOffset = 0;
        }
        _showCpuMetrics = showCpu;
        _showGpuMetrics = showGpu;
    }

    public void ApplyNetworkPeakWindow(int seconds)
    {
        int normalized = Math.Clamp(seconds, 10, 60);
        NetworkPeakLabel = $"{normalized}s max";
        if (_networkPeakWindowSeconds == normalized) return;
        _networkPeakWindowSeconds = normalized;
        _networkRxPeak = new RecentPeakTracker(TimeSpan.FromSeconds(normalized));
        _networkTxPeak = new RecentPeakTracker(TimeSpan.FromSeconds(normalized));
        NetworkPeakRx = NetworkPeakTx = BytesPerSecondFormatter.WarmingUp;
    }

    private void ResetPeakState()
    {
        ResetMetricPeakState();
        ResetNetworkPeakState();
    }

    private void ResetMetricPeakState()
    {
        foreach (RecentPeakTracker tracker in new[] { _cpuPeak, _memoryPeak, _gpuPeak, _diskPeak })
        {
            tracker.Clear();
        }
        CpuPeakOffset = MemoryPeakOffset = GpuPeakOffset = DiskPeakOffset = 0;
    }

    private void ResetNetworkPeakState()
    {
        _networkRxPeak.Clear();
        _networkTxPeak.Clear();
        NetworkPeakRx = NetworkPeakTx = BytesPerSecondFormatter.WarmingUp;
    }

    private void ApplyDisk(DiskSnapshot disk, RateUnitSystem units)
    {
        if (disk.Status != MetricStatus.Ok)
        {
            DiskValue = disk.Status == MetricStatus.WarmingUp ? PercentFormatter.WarmingUp : PercentFormatter.Unavailable;
            DiskRead = disk.Status == MetricStatus.WarmingUp ? BytesPerSecondFormatter.WarmingUp : BytesPerSecondFormatter.Unavailable;
            DiskWrite = DiskRead;
            DiskBarFraction = 0;
            DiskStatusMessage = DescribeDiskAvailability(disk.AvailabilityReason);
            return;
        }
        DiskStatusMessage = disk.SelectedDiskLabel;
        (DiskValue, _, DiskBarFraction) = FormatPercent(disk.ActivePercent);
        DiskRead = BytesPerSecondFormatter.Format(disk.ReadBytesPerSecond, units);
        DiskWrite = BytesPerSecondFormatter.Format(disk.WriteBytesPerSecond, units);
    }

    internal static string DescribeDiskAvailability(DiskAvailabilityReason reason) => reason switch
    {
        DiskAvailabilityReason.SystemDiskResolveFailed => "Windowsがある物理ディスクを自動判定できません。設定から明示選択してください。",
        DiskAvailabilityReason.SystemVolumeSpansMultipleDisks => "Windowsボリュームが複数ディスクにまたがるため自動選択できません。",
        DiskAvailabilityReason.RequestedDiskNotFound => "選択した物理ディスクが見つかりません。設定を確認してください。",
        DiskAvailabilityReason.PdhEnumerationFailed => "物理ディスクの一覧を取得できません。",
        DiskAvailabilityReason.CounterOpenFailed => "物理ディスクの計測を開始できません。",
        DiskAvailabilityReason.CounterReadFailed => "物理ディスクの計測値を読み取れません。",
        DiskAvailabilityReason.CollectionFailed => "物理ディスクの計測値を収集できません。",
        _ => string.Empty,
    };

    private void ApplyBattery(MetricSnapshot snapshot, AppSettings settings)
    {
        BatterySnapshot battery = snapshot.Battery;
        BatteryRuntimeEstimate estimate = _batteryEstimator.Add(snapshot.TakenAt, battery);
        int targetPercent = settings.EffectiveBatteryChargeTargetPercent;
        BatteryChargeTimeEstimate chargeEstimate = _batteryChargeEstimator.Add(
            snapshot.TakenAt,
            battery,
            targetPercent);
        string percent = double.IsFinite(battery.Percent) ? $"{battery.Percent:F0}%" : "N/A";
        BatteryFlowMarker = battery.PowerState switch
        {
            BatteryPowerState.Charging => "→\u200A🔋",
            BatteryPowerState.Discharging => "🔋\u200A→",
            _ => string.Empty,
        };
        switch (battery.PowerState)
        {
            case BatteryPowerState.Discharging:
                BatteryPrimary = estimate.Remaining is { } remaining ? FormatBatteryDuration(remaining) : "残り N/A";
                BatterySecondary = double.IsFinite(estimate.SmoothedDischargeMilliwatts)
                    ? $"{percent} · {PowerFormatter.FormatWatts(estimate.SmoothedDischargeMilliwatts / 1000d)}"
                    : $"{percent} · N/A W";
                break;
            case BatteryPowerState.Charging:
                bool targetOrAbove = double.IsFinite(battery.Percent) &&
                    Math.Round(battery.Percent, MidpointRounding.AwayFromZero) >= targetPercent;
                BatteryPrimary = !targetOrAbove &&
                    chargeEstimate.Status == MetricStatus.Ok && chargeEstimate.Remaining is { } chargeRemaining
                    ? FormatBatteryDuration(chargeRemaining)
                    : "充電中";
                double? chargePower = chargeEstimate.Status == MetricStatus.Ok
                    ? chargeEstimate.SmoothedChargeMilliwatts
                    : chargeEstimate.Status == MetricStatus.Unavailable
                        ? double.NaN
                        : null;
                BatterySecondary = targetOrAbove
                    ? BatteryDisplayFormatter.ChargingAtOrAboveTargetSecondary(battery.Percent, targetPercent)
                    : chargeEstimate.Status == MetricStatus.Unavailable &&
                        double.IsFinite(battery.Percent) && battery.Percent >= 5 && battery.Percent < targetPercent
                        ? $"{percent}→{targetPercent}% · N/A"
                        : BatteryDisplayFormatter.ChargingSecondary(
                        battery.Percent,
                        targetPercent,
                        chargePower,
                        settings.FontFamilyName);
                break;
            case BatteryPowerState.AcConnected:
                bool targetReached = double.IsFinite(battery.Percent) &&
                    Math.Round(battery.Percent, MidpointRounding.AwayFromZero) >= targetPercent;
                BatteryPrimary = targetReached ? "上限到達" : "AC接続";
                BatterySecondary = targetReached
                    ? BatteryDisplayFormatter.ReachedSecondary(
                        battery.Percent,
                        settings.EffectiveBatteryChargeTargetSource)
                    : percent;
                break;
            default:
                BatteryPrimary = "状態 N/A";
                BatterySecondary = percent;
                break;
        }
    }

    private static string FormatBatteryDuration(TimeSpan duration) =>
        DurationFormatter.FormatApproximate(duration).TrimStart('≈');

    private void ApplyPeaks(
        MetricSnapshot snapshot,
        RateUnitSystem units,
        bool showMetricPeaks,
        bool showNetworkPeaks)
    {
        if (showMetricPeaks)
        {
            if (snapshot.Cpu.UtilizationStatus == MetricStatus.Ok) _cpuPeak.Add(snapshot.TakenAt, snapshot.Cpu.UtilizationPercent);
            if (snapshot.Memory.Status == MetricStatus.Ok) _memoryPeak.Add(snapshot.TakenAt, snapshot.Memory.UtilizationPercent);
            if (snapshot.Gpu.DisplayAdapter?.UtilizationStatus == MetricStatus.Ok) _gpuPeak.Add(snapshot.TakenAt, snapshot.Gpu.DisplayAdapter.UtilizationPercent);
            if (snapshot.Disk.Status == MetricStatus.Ok) _diskPeak.Add(snapshot.TakenAt, snapshot.Disk.ActivePercent);
            CpuPeakOffset = Offset(_cpuPeak.Peak);
            MemoryPeakOffset = Offset(_memoryPeak.Peak);
            GpuPeakOffset = Offset(_gpuPeak.Peak);
            DiskPeakOffset = Offset(_diskPeak.Peak);
        }
        else
        {
            ResetMetricPeakState();
        }

        if (showNetworkPeaks)
        {
            if (snapshot.Network.AggregateStatus == MetricStatus.Ok)
            {
                _networkRxPeak.Add(snapshot.TakenAt, snapshot.Network.AggregateBytesReceivedPerSecond);
                _networkTxPeak.Add(snapshot.TakenAt, snapshot.Network.AggregateBytesSentPerSecond);
            }
            NetworkPeakRx = double.IsFinite(_networkRxPeak.Peak) ? BytesPerSecondFormatter.Format(_networkRxPeak.Peak, units) : BytesPerSecondFormatter.WarmingUp;
            NetworkPeakTx = double.IsFinite(_networkTxPeak.Peak) ? BytesPerSecondFormatter.Format(_networkTxPeak.Peak, units) : BytesPerSecondFormatter.WarmingUp;
        }
        else
        {
            ResetNetworkPeakState();
        }
    }

    private static double Offset(double percent) => double.IsFinite(percent) ? Math.Clamp(percent / 100d * 225d, 0d, 225d) : 0d;

    private static string FormatTemperature(MetricStatus status, double value) => status switch
    {
        MetricStatus.WarmingUp => TemperatureFormatter.WarmingUp,
        MetricStatus.Ok => TemperatureFormatter.FormatCelsius(value),
        _ => TemperatureFormatter.Unavailable,
    };

    private static string FormatGpuTemperature(GpuSnapshot gpu, TemperatureSnapshot temperature)
    {
        if (temperature.GpuCollectionStatus != MetricStatus.Ok) return FormatTemperature(temperature.GpuCollectionStatus, double.NaN);
        string? name = gpu.DisplayAdapter?.DisplayName;
        GpuTemperatureReading[] matches = temperature.GpuReadings.Where(r => name is not null && NormalizeHardwareName(r.DisplayName) == NormalizeHardwareName(name)).ToArray();
        GpuTemperatureReading? reading = matches.Length == 1 ? matches[0] : null;
        return reading is null ? TemperatureFormatter.Unavailable : FormatTemperature(reading.Status, reading.Celsius);
    }

    private static (string Value, string Unit, double Fraction) FormatCpuUtilization(CpuSnapshot cpu) => cpu.UtilizationStatus switch
    {
        MetricStatus.WarmingUp => (PercentFormatter.WarmingUp, string.Empty, 0d),
        MetricStatus.Unavailable => (PercentFormatter.Unavailable, string.Empty, 0d),
        _ => FormatPercent(cpu.UtilizationPercent),
    };

    private static string FormatCpuFrequency(CpuSnapshot cpu) => cpu.FrequencyStatus switch
    {
        MetricStatus.WarmingUp => FrequencyFormatter.WarmingUp,
        MetricStatus.Unavailable => FrequencyFormatter.Unavailable,
        _ => FrequencyFormatter.FormatGhz(cpu.FrequencyMhz) + (cpu.FrequencyIsEstimate ? "*" : ""),
    };

    private static (string Value, string Unit, double Fraction, string Usage) FormatMemory(MemorySnapshot memory) => memory.Status switch
    {
        MetricStatus.WarmingUp => (PercentFormatter.WarmingUp, string.Empty, 0d, BytesFormatter.Unavailable),
        MetricStatus.Unavailable => (PercentFormatter.Unavailable, string.Empty, 0d, BytesFormatter.Unavailable),
        _ => FormatMemoryOk(memory),
    };

    private static (string Value, string Unit, double Fraction, string Memory) FormatGpu(GpuSnapshot gpu)
    {
        if (gpu.OverallStatus == MetricStatus.WarmingUp)
        {
            return (PercentFormatter.WarmingUp, string.Empty, 0d, BytesFormatter.Unavailable);
        }
        if (gpu.OverallStatus == MetricStatus.Unavailable || gpu.Adapters.IsDefaultOrEmpty)
        {
            return (PercentFormatter.Unavailable, string.Empty, 0d, BytesFormatter.Unavailable);
        }
        var adapter = gpu.DisplayAdapter ?? gpu.PrimaryAdapter!;
        (string value, string unit, double fraction) = adapter.UtilizationStatus switch
        {
            MetricStatus.WarmingUp => (PercentFormatter.WarmingUp, string.Empty, 0d),
            MetricStatus.Unavailable => (PercentFormatter.Unavailable, string.Empty, 0d),
            _ => FormatPercent(adapter.UtilizationPercent),
        };
        string memory = adapter.MemoryStatus switch
        {
            MetricStatus.Ok when adapter.DedicatedLimitBytes > 0 =>
                BytesFormatter.FormatCompactMemoryPair(adapter.DedicatedUsageBytes, adapter.DedicatedLimitBytes),
            MetricStatus.Ok => BytesFormatter.FormatBinaryMemory(adapter.DedicatedUsageBytes),
            _ => BytesFormatter.Unavailable,
        };
        return (value, unit, fraction, memory);
    }

    private static (string Value, string Unit, double Fraction) FormatPercent(double percent)
    {
        (string value, string unit) = PercentFormatter.FormatParts(percent, decimals: 0);
        return (value, unit, PercentFormatter.ToFraction(percent));
    }

    private static string FormatGpuPower(GpuSnapshot gpu, PowerSnapshot power)
    {
        if (power.GpuCollectionStatus == MetricStatus.WarmingUp)
        {
            return PowerFormatter.WarmingUp;
        }
        if (power.GpuCollectionStatus == MetricStatus.Unavailable || power.GpuReadings.IsDefaultOrEmpty)
        {
            return PowerFormatter.Unavailable;
        }

        GpuPowerReading? reading = null;
        string? adapterName = gpu.DisplayAdapter?.DisplayName;
        if (!string.IsNullOrWhiteSpace(adapterName))
        {
            string normalizedAdapter = NormalizeHardwareName(adapterName);
            GpuPowerReading[] matches = power.GpuReadings
                .Where(candidate => NormalizeHardwareName(candidate.DisplayName) == normalizedAdapter)
                .ToArray();
            if (matches.Length == 1)
            {
                reading = matches[0];
            }
        }
        if (reading is null && power.GpuReadings.Length == 1)
        {
            reading = power.GpuReadings[0];
        }

        return reading is null
            ? PowerFormatter.Unavailable
            : FormatPower(reading.Status, reading.Watts);
    }

    private static string FormatPower(MetricStatus status, double watts) => status switch
    {
        MetricStatus.WarmingUp => PowerFormatter.WarmingUp,
        MetricStatus.Unavailable => PowerFormatter.Unavailable,
        _ => PowerFormatter.FormatWatts(watts),
    };

    private static string NormalizeHardwareName(string value) => string.Concat(
        value.Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant));

    private static (string Value, string Unit, double Fraction, string Usage) FormatMemoryOk(MemorySnapshot memory)
    {
        (string value, string unit, double fraction) = FormatPercent(memory.UtilizationPercent);
        return (value, unit, fraction, BytesFormatter.FormatCompactMemoryPair(memory.UsedBytes, memory.TotalBytes));
    }

    private static (string Rx, string Tx) FormatNetwork(NetworkSnapshot net, RateUnitSystem system)
    {
        if (net.AggregateStatus == MetricStatus.WarmingUp)
        {
            return (BytesPerSecondFormatter.WarmingUp, BytesPerSecondFormatter.WarmingUp);
        }
        if (net.AggregateStatus == MetricStatus.Unavailable)
        {
            return (BytesPerSecondFormatter.Unavailable, BytesPerSecondFormatter.Unavailable);
        }
        return (
            BytesPerSecondFormatter.Format(net.AggregateBytesReceivedPerSecond, system),
            BytesPerSecondFormatter.Format(net.AggregateBytesSentPerSecond, system));
    }

    private void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return;
        }
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
