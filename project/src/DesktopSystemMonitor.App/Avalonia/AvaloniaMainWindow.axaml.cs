using Avalonia.Controls;
using Avalonia.Interactivity;

using DesktopSystemMonitor.Core.Metrics;
using DesktopSystemMonitor.Mac;

namespace DesktopSystemMonitor.App;

public sealed partial class AvaloniaMainWindow : Window
{
    private readonly IMetricSource<CpuSnapshot> _cpuSource;
    private readonly IMetricSource<MemorySnapshot> _memorySource;
    private readonly IGpuMetricSource _gpuSource;

    public AvaloniaMainWindow()
        : this(new MacMetricSourceFactory())
    {
    }

    internal AvaloniaMainWindow(IMetricSourceFactory sourceFactory)
    {
        ArgumentNullException.ThrowIfNull(sourceFactory);
        _cpuSource = sourceFactory.CreateCpu();
        _memorySource = sourceFactory.CreateMemory();
        _gpuSource = sourceFactory.CreateGpu();
        InitializeComponent();
    }

    private async void OnRefreshClick(object? sender, RoutedEventArgs e)
    {
        try
        {
            CpuSnapshot cpu = await _cpuSource.SampleAsync(CancellationToken.None);
            MemorySnapshot memory = await _memorySource.SampleAsync(CancellationToken.None);
            GpuSnapshot gpu = await _gpuSource.SampleAsync(CancellationToken.None);

            CpuText.Text = FormatPercent("CPU", cpu.UtilizationStatus, cpu.UtilizationPercent);
            MemoryText.Text = FormatPercent("メモリ", memory.Status, memory.UtilizationPercent);
            GpuText.Text = FormatPercent("GPU", gpu.OverallStatus, gpu.DisplayAdapter?.UtilizationPercent ?? double.NaN);
            StatusText.Text = cpu.UtilizationStatus == MetricStatus.Ok
                && memory.Status == MetricStatus.Ok
                && gpu.OverallStatus == MetricStatus.Ok
                ? "ネイティブ指標: 更新済み"
                : "ネイティブ指標: 未接続（N/A）";
        }
        catch (Exception)
        {
            CpuText.Text = "CPU: N/A";
            MemoryText.Text = "メモリ: N/A";
            GpuText.Text = "GPU: N/A";
            StatusText.Text = "ネイティブ指標: 未接続（N/A）";
        }
    }

    private static string FormatPercent(string label, MetricStatus status, double value) =>
        status == MetricStatus.Ok && double.IsFinite(value)
            ? $"{label}: {value:0.0}%"
            : $"{label}: N/A";
}
