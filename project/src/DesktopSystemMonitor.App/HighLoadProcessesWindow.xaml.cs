using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using DesktopSystemMonitor.Core.Formatting;
using DesktopSystemMonitor.Windows.Processes;

namespace DesktopSystemMonitor.App;

public partial class HighLoadProcessesWindow : Window, IDisposable
{
    private readonly ProcessLoadSampler _sampler;
    private readonly DispatcherTimer _timer;
    private readonly CancellationTokenSource _lifetimeCts = new();
    private CancellationTokenSource? _sampleCts;
    private ProcessLoadSort _sort;
    private bool _refreshing;
    private bool _refreshRequested;
    private bool _resetBeforeRefresh;
    private bool _suspended;
    private bool _closed;
    private bool _lifetimeCtsDisposed;
    private bool _samplerDisposed;

    public HighLoadProcessesWindow() : this(new ProcessLoadSampler())
    {
    }

    internal HighLoadProcessesWindow(ProcessLoadSampler sampler)
    {
        InitializeComponent();
        _sampler = sampler;
        _timer = new DispatcherTimer(
            TimeSpan.FromSeconds(2),
            DispatcherPriority.Background,
            async (_, _) => await RefreshRowsAsync().ConfigureAwait(true),
            Dispatcher);
        Loaded += (_, _) => RequestRefresh(resetBaseline: true);
        Closed += (_, _) => CloseSampling();
    }

    internal void SetSuspended(bool suspended)
    {
        if (_closed || _suspended == suspended)
        {
            return;
        }
        _suspended = suspended;
        if (suspended)
        {
            _timer.Stop();
            _sampleCts?.Cancel();
            StatusText.Text = "一時停止中";
        }
        else if (IsLoaded)
        {
            RequestRefresh(resetBaseline: true);
        }
    }

    private async Task RefreshRowsAsync()
    {
        if (_refreshing || _suspended || _closed)
        {
            return;
        }
        _refreshing = true;
        _timer.Stop();
        using var sampleCts = CancellationTokenSource.CreateLinkedTokenSource(_lifetimeCts.Token);
        _sampleCts = sampleCts;
        try
        {
            ProcessLoadSort requestedSort = _sort;
            IReadOnlyList<ProcessLoadRow> rows = await Task.Run(
                () => _sampler.Sample(requestedSort, sampleCts.Token),
                sampleCts.Token).ConfigureAwait(true);
            if (_suspended || _closed || sampleCts.IsCancellationRequested)
            {
                return;
            }
            ProcessGrid.ItemsSource = rows.Select(row => new DisplayRow(
                row.Name,
                row.ProcessId,
                double.IsFinite(row.CpuPercent) ? $"{row.CpuPercent:F1}" : "--",
                row.PrivateWorkingSetBytes is long memoryBytes
                    ? BytesFormatter.FormatBinaryMemory(memoryBytes)
                    : "--",
                double.IsFinite(row.ReadBytesPerSecond) ? BytesPerSecondFormatter.Format(row.ReadBytesPerSecond) : "--",
                double.IsFinite(row.WriteBytesPerSecond) ? BytesPerSecondFormatter.Format(row.WriteBytesPerSecond) : "--")).ToArray();
            _timer.Interval = _sampler.NextInterval;
            StatusText.Text = $"更新間隔 {_timer.Interval.TotalSeconds:F0}秒";
        }
        catch (OperationCanceledException) when (sampleCts.IsCancellationRequested)
        {
            // Closing, suspension, and a requested refresh all cancel cooperatively.
        }
        catch
        {
            StatusText.Text = "取得不可";
        }
        finally
        {
            if (ReferenceEquals(_sampleCts, sampleCts))
            {
                _sampleCts = null;
            }
            _refreshing = false;
            DisposeLifetimeCtsIfIdle();
            if (!_closed && !_suspended && _refreshRequested)
            {
                bool reset = _resetBeforeRefresh;
                _refreshRequested = false;
                _resetBeforeRefresh = false;
                if (reset)
                {
                    _sampler.Reset();
                }
                _ = RefreshRowsAsync();
            }
            else if (!_closed && !_suspended)
            {
                _timer.Interval = _sampler.NextInterval;
                _timer.Start();
            }
        }
    }

    private void SortBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (SortBox.SelectedItem is ComboBoxItem item && Enum.TryParse(item.Tag?.ToString(), out ProcessLoadSort sort))
        {
            _sort = sort;
            if (IsLoaded)
            {
                RequestRefresh(resetBaseline: true);
            }
        }
    }

    private void RequestRefresh(bool resetBaseline)
    {
        if (_closed || _suspended)
        {
            return;
        }
        if (_refreshing)
        {
            _refreshRequested = true;
            _resetBeforeRefresh |= resetBaseline;
            _sampleCts?.Cancel();
            return;
        }
        if (resetBaseline)
        {
            _sampler.Reset();
        }
        _ = RefreshRowsAsync();
    }

    private void CloseSampling()
    {
        if (_closed)
        {
            return;
        }
        _closed = true;
        _timer.Stop();
        _lifetimeCts.Cancel();
        _sampleCts?.Cancel();
        DisposeLifetimeCtsIfIdle();
    }

    private void DisposeLifetimeCtsIfIdle()
    {
        if (_closed && !_refreshing && !_lifetimeCtsDisposed)
        {
            _lifetimeCts.Dispose();
            _lifetimeCtsDisposed = true;
        }
        if (_closed && !_refreshing && !_samplerDisposed)
        {
            _sampler.Dispose();
            _samplerDisposed = true;
        }
    }

    public void Dispose()
    {
        CloseSampling();
        GC.SuppressFinalize(this);
    }

    private sealed record DisplayRow(string Name, int ProcessId, string CpuDisplay, string MemoryDisplay, string ReadDisplay, string WriteDisplay);
}
