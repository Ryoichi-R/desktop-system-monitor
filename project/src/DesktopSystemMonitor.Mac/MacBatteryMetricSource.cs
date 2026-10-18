using DesktopSystemMonitor.Core.Metrics;

namespace DesktopSystemMonitor.Mac;

/// <summary>Mac Studioはバッテリーを持たないため、macOSでは常にAbsentを返す。</summary>
public sealed class MacBatteryMetricSource : IBatteryMetricSource
{
    public ValueTask<BatterySnapshot> SampleAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(BatterySnapshot.Absent());
    }

    public void ResetBaseline()
    {
    }

    public void SetEnabled(bool enabled)
    {
    }

    public void Dispose()
    {
    }
}
