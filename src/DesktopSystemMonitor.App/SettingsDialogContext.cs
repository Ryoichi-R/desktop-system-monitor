using System.Collections.Immutable;
using DesktopSystemMonitor.Core.Metrics;
using DesktopSystemMonitor.Core.Settings;

namespace DesktopSystemMonitor.App;

internal sealed record SettingsDialogContext(
    OptionalTelemetryCapabilities Capabilities,
    ImmutableArray<int> AvailableDiskNumbers,
    string DiskStatusMessage)
{
    internal static SettingsDialogContext Unknown { get; } = new(
        OptionalTelemetryCapabilities.Unknown,
        ImmutableArray<int>.Empty,
        "現在の取得状態はまだ確認できません。");

    internal static SettingsDialogContext Create(
        OptionalTelemetryCapabilities capabilities,
        AppSettings settings,
        DiskSnapshot? disk,
        ImmutableArray<int> availableDiskNumbers,
        bool diskEnumerationFailed)
    {
        string status = diskEnumerationFailed
            ? MetricViewModel.DescribeDiskAvailability(DiskAvailabilityReason.PdhEnumerationFailed)
            : DescribeCurrentDiskStatus(settings.ShowDiskMetrics, disk);
        return new SettingsDialogContext(capabilities, availableDiskNumbers, status);
    }

    private static string DescribeCurrentDiskStatus(bool enabled, DiskSnapshot? disk)
    {
        if (!enabled)
        {
            return "DISK表示は現在無効です。";
        }
        if (disk is null)
        {
            return "現在の取得状態はまだ確認できません。";
        }
        if (disk.Status == MetricStatus.Ok)
        {
            return $"現在の取得状態: {disk.SelectedDiskLabel}";
        }
        if (disk.Status == MetricStatus.WarmingUp)
        {
            return $"現在の取得状態: 準備中（{disk.SelectedDiskLabel}）";
        }
        string reason = MetricViewModel.DescribeDiskAvailability(disk.AvailabilityReason);
        return string.IsNullOrWhiteSpace(reason) ? "物理ディスクの取得状態を確認できません。" : reason;
    }
}
