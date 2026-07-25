using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using DesktopSystemMonitor.Core.Settings;
using DesktopSystemMonitor.Windows.Window;

namespace DesktopSystemMonitor.App;

public partial class BackgroundWindow : Window
{
    private readonly IWindowLayerApi _layerApi;
    private readonly WindowLayerRepairEngine _layerRepair;
    private readonly DispatcherTimer _repairTimer;
    private readonly Action<string, Exception?> _recordDiagnostic;
    private HwndSource? _hwndSource;
    private IntPtr _sourceHwnd;
    private bool _presentationRequested;
    private bool _repairSuspended;
    private bool _layerFailureSuppressed;
    private bool _closing;

    internal BackgroundWindow(
        Action<string, Exception?> recordDiagnostic,
        IWindowLayerApi? layerApi = null)
    {
        _recordDiagnostic = recordDiagnostic ?? throw new ArgumentNullException(nameof(recordDiagnostic));
        _layerApi = layerApi ?? NativeWindowLayerApi.Instance;
        _layerRepair = new WindowLayerRepairEngine(_layerApi, RecordLayerDiagnostic);
        InitializeComponent();
        SourceInitialized += OnSourceInitialized;
        Closed += OnClosed;
        _repairTimer = new DispatcherTimer(
            TimeSpan.FromSeconds(2),
            DispatcherPriority.Background,
            (_, _) => RunRepairTick(),
            Dispatcher);
        _repairTimer.Stop();
    }

    internal bool PresentationRequested => _presentationRequested;
    internal bool LayerFailureSuppressed => _layerFailureSuppressed;
    internal IntPtr Handle => _sourceHwnd;

    internal void ApplyAppearance(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        BackgroundBrushFactory.Apply(BackgroundFadeHost, BackgroundRoot, settings);
    }

    internal void SetPresentationRequested(bool requested, LayerRepairTrigger trigger)
    {
        Dispatcher.VerifyAccess();
        _presentationRequested = requested;
        if (!requested)
        {
            Hide();
            return;
        }

        if (!IsVisible)
        {
            Show();
        }
        ReapplyBottomMost(trigger, forceApply: true, countFailure: false);
    }

    internal void SetRepairSuspended(bool suspended)
    {
        Dispatcher.VerifyAccess();
        _repairSuspended = suspended;
        if (!suspended && _presentationRequested && IsVisible)
        {
            ReapplyBottomMost(LayerRepairTrigger.SessionUnlock, forceApply: true, countFailure: false);
        }
    }

    internal void ReapplyBottomMost(LayerRepairTrigger trigger)
    {
        Dispatcher.VerifyAccess();
        ReapplyBottomMost(trigger, forceApply: true, countFailure: false);
    }

    internal LayerRepairResult RunRepairTick()
    {
        Dispatcher.VerifyAccess();
        return ReapplyBottomMost(
            LayerRepairTrigger.Timer,
            forceApply: false,
            countFailure: true);
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        _sourceHwnd = new WindowInteropHelper(this).Handle;
        if (_sourceHwnd == IntPtr.Zero)
        {
            return;
        }
        _hwndSource = HwndSource.FromHwnd(_sourceHwnd);
        _hwndSource?.AddHook(WindowProc);
        try
        {
            ClickThroughHelper.SetClickThrough(_sourceHwnd, clickThrough: true);
        }
        catch (Exception ex)
        {
            _recordDiagnostic("background-layer-style-failure", ex);
        }
        ReapplyBottomMost(LayerRepairTrigger.SourceInitialized, forceApply: true, countFailure: false);
        _repairTimer.Start();
    }

    private IntPtr WindowProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == BottomMostStrategy.TaskbarCreatedMessage)
        {
            ReapplyBottomMost(LayerRepairTrigger.TaskbarCreated, forceApply: true, countFailure: false);
        }
        else if (msg == 0x007E) // WM_DISPLAYCHANGE
        {
            ReapplyBottomMost(LayerRepairTrigger.DisplayChanged, forceApply: true, countFailure: false);
        }
        else if (msg == 0x0046 && !_repairSuspended) // WM_WINDOWPOSCHANGING
        {
            try
            {
                _ = BottomMostStrategy.RewriteWindowPosForLayer(
                    lParam,
                    LayerStrategy.BottomMost,
                    suppressRewrite: false,
                    _layerApi);
            }
            catch (Exception ex)
            {
                _recordDiagnostic("background-layer-window-position-failure", ex);
            }
        }
        return IntPtr.Zero;
    }

    private LayerRepairResult ReapplyBottomMost(
        LayerRepairTrigger trigger,
        bool forceApply,
        bool countFailure)
    {
        if (_closing
            || !_presentationRequested
            || _repairSuspended
            || _sourceHwnd == IntPtr.Zero)
        {
            return SkippedResult();
        }

        LayerRepairResult result;
        try
        {
            result = _layerRepair.Repair(
                _sourceHwnd,
                LayerStrategy.BottomMost,
                trigger,
                countFailure,
                forceApply);
        }
        catch (Exception ex)
        {
            result = _layerRepair.RecordManagedException(LayerStrategy.BottomMost, trigger);
            _recordDiagnostic("background-layer-managed-failure", ex);
        }

        if (result.Outcome == LayerRepairOutcome.FallbackPending)
        {
            if (!_layerFailureSuppressed)
            {
                _layerFailureSuppressed = true;
                Opacity = 0;
                _recordDiagnostic("background-layer-suppressed", null);
            }
        }
        else if (result.Outcome == LayerRepairOutcome.Healthy && _layerFailureSuppressed)
        {
            _layerFailureSuppressed = false;
            Opacity = 1;
            _recordDiagnostic("background-layer-recovered", null);
        }

        return result;
    }

    private void RecordLayerDiagnostic(LayerDiagnosticEvent diagnostic) =>
        _recordDiagnostic($"background-layer-{diagnostic.Category.ToString().ToLowerInvariant()}", null);

    private static LayerRepairResult SkippedResult() => new(
        LayerRepairOutcome.Skipped,
        new LayerApplyResult(LayerStrategy.BottomMost, false, null, 0, LayerFailureKind.None),
        0);

    private void OnClosed(object? sender, EventArgs e)
    {
        _closing = true;
        _repairTimer.Stop();
        _hwndSource?.RemoveHook(WindowProc);
        _hwndSource = null;
        _sourceHwnd = IntPtr.Zero;
        _layerRepair.ResetEpisode();
    }
}
