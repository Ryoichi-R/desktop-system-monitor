using System;
using System.Collections.Immutable;
using DesktopSystemMonitor.Core.Metrics;
using DesktopSystemMonitor.Core.Settings;
using Xunit;

namespace DesktopSystemMonitor.App.Tests;

public sealed class SettingsDialogContextTests
{
    [Fact]
    public void displays_the_current_disk_failure_reason()
    {
        var disk = DiskSnapshot.Unavailable(
            "自動選択不可",
            DiskAvailabilityReason.SystemVolumeSpansMultipleDisks);

        SettingsDialogContext context = SettingsDialogContext.Create(
            OptionalTelemetryCapabilities.Unknown,
            new AppSettings { ShowDiskMetrics = true },
            disk,
            ImmutableArray<int>.Empty,
            diskEnumerationFailed: false);

        Assert.Contains("複数ディスク", context.DiskStatusMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void reports_enumeration_failure_instead_of_a_stale_snapshot()
    {
        SettingsDialogContext context = SettingsDialogContext.Create(
            OptionalTelemetryCapabilities.Unknown,
            new AppSettings { ShowDiskMetrics = true },
            DiskSnapshot.Warmup(),
            ImmutableArray<int>.Empty,
            diskEnumerationFailed: true);

        Assert.Equal("物理ディスクの一覧を取得できません。", context.DiskStatusMessage);
    }
}
