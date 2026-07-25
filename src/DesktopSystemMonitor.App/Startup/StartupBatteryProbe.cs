using DesktopSystemMonitor.Core.Metrics;

namespace DesktopSystemMonitor.App.Startup;

internal static class StartupBatteryProbe
{
    internal static bool TryConsume(
        ValueTask<BatterySnapshot> probe,
        Action<string, Exception?> recordDiagnostic,
        out BatterySnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(recordDiagnostic);
        if (probe.IsCompletedSuccessfully)
        {
            snapshot = probe.Result;
            return true;
        }

        _ = ObserveAsync(probe.AsTask(), recordDiagnostic);
        snapshot = BatterySnapshot.Unavailable();
        return false;
    }

    internal static async Task ObserveAsync(
        Task<BatterySnapshot> probe,
        Action<string, Exception?> recordDiagnostic)
    {
        try
        {
            _ = await probe.ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            try
            {
                recordDiagnostic("battery-startup-probe-failure", ex);
            }
            catch
            {
                // Startup must remain fire-and-forget even if diagnostics is shutting down.
            }
        }
    }
}
