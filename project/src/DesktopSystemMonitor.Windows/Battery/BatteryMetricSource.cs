using System.Runtime.InteropServices;
using DesktopSystemMonitor.Core.Metrics;

namespace DesktopSystemMonitor.Windows.Battery;

public interface IBatteryMetricSource : IMetricSource<BatterySnapshot>
{
    void SetEnabled(bool enabled);
}

public sealed class BatteryMetricSource : IBatteryMetricSource
{
    private readonly Func<(bool Success, PowerStatusInterop.SystemPowerStatus Value)> _readGeneral;
    private readonly Func<(bool Success, PowerStatusInterop.SystemBatteryStateValue Value)> _readDetailed;
    private bool _enabled;

    public BatteryMetricSource() : this(ReadGeneral, ReadDetailed) { }

    internal BatteryMetricSource(
        Func<(bool Success, PowerStatusInterop.SystemPowerStatus Value)> readGeneral,
        Func<(bool Success, PowerStatusInterop.SystemBatteryStateValue Value)> readDetailed)
    {
        _readGeneral = readGeneral;
        _readDetailed = readDetailed;
    }

    public void SetEnabled(bool enabled) => _enabled = enabled;

    public ValueTask<BatterySnapshot> SampleAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!_enabled)
        {
            return ValueTask.FromResult(BatterySnapshot.Unavailable());
        }
        var general = _readGeneral();
        var detailed = _readDetailed();
        if (!general.Success || !detailed.Success)
        {
            return ValueTask.FromResult(BatterySnapshot.Unavailable());
        }
        return ValueTask.FromResult(Map(general.Value, detailed.Value));
    }

    internal static BatterySnapshot Map(PowerStatusInterop.SystemPowerStatus general, PowerStatusInterop.SystemBatteryStateValue detailed)
    {
        if (detailed.BatteryPresent == 0 || general.BatteryFlag == 128)
        {
            return BatterySnapshot.Absent();
        }
        double percent = general.BatteryLifePercent == byte.MaxValue ? double.NaN : general.BatteryLifePercent;
        double capacity = detailed.RemainingCapacity == uint.MaxValue ? double.NaN : detailed.RemainingCapacity;
        double rate = detailed.Rate == int.MinValue ? double.NaN : detailed.Rate;
        TimeSpan? estimated = detailed.EstimatedTime == uint.MaxValue ? null : TimeSpan.FromSeconds(detailed.EstimatedTime);
        bool charging = detailed.Charging != 0;
        bool discharging = detailed.Discharging != 0;
        bool contradiction = charging && discharging || charging && rate < 0 || discharging && rate > 0;
        BatteryPowerState state = contradiction ? BatteryPowerState.Unknown
            : charging ? BatteryPowerState.Charging
            : discharging ? BatteryPowerState.Discharging
            : detailed.AcOnLine != 0 || general.AcLineStatus == 1 ? BatteryPowerState.AcConnected
            : BatteryPowerState.Unknown;
        bool rateValid = double.IsFinite(rate) && rate != 0;
        MetricStatus status = state == BatteryPowerState.Unknown || (state == BatteryPowerState.Discharging && !rateValid)
            ? MetricStatus.Unavailable
            : MetricStatus.Ok;
        return new BatterySnapshot
        {
            Status = status,
            PowerState = state,
            BatteryPresent = true,
            Percent = percent,
            RemainingCapacityMilliwattHours = capacity,
            RateMilliwatts = rate,
            WindowsEstimatedTime = estimated,
        };
    }

    private static (bool, PowerStatusInterop.SystemPowerStatus) ReadGeneral() =>
        (PowerStatusInterop.GetSystemPowerStatus(out var value), value);

    private static (bool, PowerStatusInterop.SystemBatteryStateValue) ReadDetailed()
    {
        uint result = PowerStatusInterop.CallNtPowerInformation(
            PowerStatusInterop.SystemBatteryState,
            IntPtr.Zero,
            0,
            out var value,
            checked((uint)Marshal.SizeOf<PowerStatusInterop.SystemBatteryStateValue>()));
        return (result == 0, value);
    }

    public void ResetBaseline() { }
    public void Dispose() { }
}
