using DesktopSystemMonitor.Core.Settings;
using DesktopSystemMonitor.Windows.Window;
using Microsoft.Win32;

namespace DesktopSystemMonitor.App.Startup;

internal static class LayerLifecycleCoordinator
{
    public static void HandleLayerFallback(
        Action setRuntimeNormal,
        Action saveSettings,
        Action updateTray,
        Action<string, Exception?> recordDiagnostic)
    {
        ArgumentNullException.ThrowIfNull(setRuntimeNormal);
        ArgumentNullException.ThrowIfNull(saveSettings);
        ArgumentNullException.ThrowIfNull(updateTray);
        ArgumentNullException.ThrowIfNull(recordDiagnostic);

        TryRecordFallbackDiagnostic(recordDiagnostic, "bottom-most-fallback", null);
        RunFallbackAction(
            setRuntimeNormal,
            recordDiagnostic,
            "bottom-most-fallback-callback-failure");
        RunFallbackAction(saveSettings, recordDiagnostic, "settings-save-failure");
        RunFallbackAction(
            updateTray,
            recordDiagnostic,
            "bottom-most-fallback-callback-failure");
    }

    public static void HandleSessionSwitch(
        SessionSwitchReason reason,
        Action<bool> setSessionLocked,
        Action updateSamplingSuspension,
        Action<bool> setLayerRepairSuspended,
        Action reapplyWindowStyles)
    {
        ArgumentNullException.ThrowIfNull(setSessionLocked);
        ArgumentNullException.ThrowIfNull(updateSamplingSuspension);
        ArgumentNullException.ThrowIfNull(setLayerRepairSuspended);
        ArgumentNullException.ThrowIfNull(reapplyWindowStyles);

        if (reason == SessionSwitchReason.SessionLock)
        {
            setSessionLocked(true);
            updateSamplingSuspension();
            setLayerRepairSuspended(true);
            return;
        }

        if (reason == SessionSwitchReason.SessionUnlock)
        {
            setSessionLocked(false);
            updateSamplingSuspension();
            setLayerRepairSuspended(false);
            reapplyWindowStyles();
            return;
        }

        updateSamplingSuspension();
    }

    public static void ApplyLayerSelection(
        LayerStrategy strategy,
        Action<WindowLayerMode> updateSettings,
        Action<LayerStrategy> setWindowLayer,
        Action<LayerStrategy> updateTray)
    {
        ArgumentNullException.ThrowIfNull(updateSettings);
        ArgumentNullException.ThrowIfNull(setWindowLayer);
        ArgumentNullException.ThrowIfNull(updateTray);

        WindowLayerMode mode = strategy switch
        {
            LayerStrategy.TopMost => WindowLayerMode.AlwaysOnTop,
            LayerStrategy.Normal => WindowLayerMode.Normal,
            _ => WindowLayerMode.OnDesktop,
        };
        updateSettings(mode);
        setWindowLayer(strategy);
        updateTray(strategy);
    }

    private static void RunFallbackAction(
        Action action,
        Action<string, Exception?> recordDiagnostic,
        string failureCategory)
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
            TryRecordFallbackDiagnostic(
                recordDiagnostic,
                failureCategory,
                ex);
        }
    }

    private static void TryRecordFallbackDiagnostic(
        Action<string, Exception?> recordDiagnostic,
        string category,
        Exception? exception)
    {
        try
        {
            recordDiagnostic(category, exception);
        }
        catch
        {
            // Fallback remains best effort when its diagnostic sink is unavailable.
        }
    }
}

internal sealed class SettingsFlowCompletionScope : IDisposable
{
    private readonly Action _cleanup;
    private readonly Action _reapplyWindowStyles;
    private readonly Action<Exception> _onError;
    private int _disposed;

    public SettingsFlowCompletionScope(
        Action cleanup,
        Action reapplyWindowStyles,
        Action<Exception> onError)
    {
        _cleanup = cleanup ?? throw new ArgumentNullException(nameof(cleanup));
        _reapplyWindowStyles = reapplyWindowStyles ?? throw new ArgumentNullException(nameof(reapplyWindowStyles));
        _onError = onError ?? throw new ArgumentNullException(nameof(onError));
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        try
        {
            _cleanup();
        }
        catch (Exception ex)
        {
            TryReport(ex);
        }
        finally
        {
            try
            {
                _reapplyWindowStyles();
            }
            catch (Exception ex)
            {
                TryReport(ex);
            }
        }
    }

    private void TryReport(Exception exception)
    {
        try
        {
            _onError(exception);
        }
        catch
        {
            // Completion must stay non-throwing even if diagnostics fail.
        }
    }
}
