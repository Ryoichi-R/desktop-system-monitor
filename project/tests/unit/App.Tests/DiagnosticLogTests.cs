using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using DesktopSystemMonitor.App.Diagnostics;
using DesktopSystemMonitor.Windows.Window;
using Xunit;

namespace DesktopSystemMonitor.App.Tests;

public sealed class DiagnosticLogTests
{
    [Fact]
    public void layer_event_uses_only_fixed_allowlisted_fields()
    {
        using var log = new DiagnosticLog("unused-test-directory", enabled: false);

        log.RecordLayerEvent(new LayerDiagnosticEvent(
            LayerDiagnosticCategory.ApplyFailure,
            LayerStrategy.TopMost,
            LayerEffectiveState.NonTopMost,
            LayerRepairTrigger.Timer,
            LayerFailureKind.SetWindowPosFailed,
            5,
            2));

        string line = Assert.Single(log.Snapshot());
        Assert.Contains("category=layer-apply-failure", line, StringComparison.Ordinal);
        Assert.Contains("desired=TopMost", line, StringComparison.Ordinal);
        Assert.Contains("effective=NonTopMost", line, StringComparison.Ordinal);
        Assert.Contains("trigger=Timer", line, StringComparison.Ordinal);
        Assert.Contains("failure=SetWindowPosFailed", line, StringComparison.Ordinal);
        Assert.Contains("error=5", line, StringComparison.Ordinal);
        Assert.Contains("consecutive=2", line, StringComparison.Ordinal);
    }

    [Fact]
    public async Task full_file_queue_drops_without_blocking_and_counts_the_drop()
    {
        var writerEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseWriter = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var log = new DiagnosticLog(
            "unused-test-directory",
            enabled: true,
            fileQueueCapacity: 1,
            async _ =>
            {
                writerEntered.TrySetResult();
                await releaseWriter.Task;
            });

        log.Record("first");
        await writerEntered.Task.WaitAsync(TimeSpan.FromSeconds(2));
        log.Record("second");
        log.Record("dropped");

        Assert.Equal(1, log.DroppedFileRecordCount);
        releaseWriter.SetResult();
    }

    [Fact]
    public async Task multiple_producers_are_serialized_and_flushed()
    {
        var written = new ConcurrentQueue<string>();
        using var log = new DiagnosticLog(
            "unused-test-directory",
            enabled: true,
            fileQueueCapacity: 1_000,
            line =>
            {
                written.Enqueue(line);
                return Task.CompletedTask;
            });

        await Task.WhenAll(Enumerable.Range(0, 8).Select(producer => Task.Run(() =>
        {
            for (int index = 0; index < 50; index++)
            {
                log.Record($"producer-{producer}-event-{index}");
            }
        })));
        await log.CompleteAsync(TimeSpan.FromSeconds(2));

        Assert.Equal(400, written.Count);
        Assert.Equal(0, log.DroppedFileRecordCount);
    }

    [Fact]
    public async Task enabled_record_after_completion_is_counted_as_dropped()
    {
        using var log = new DiagnosticLog(
            "unused-test-directory",
            enabled: true,
            fileQueueCapacity: 1,
            _ => Task.CompletedTask);
        await log.CompleteAsync(TimeSpan.FromSeconds(2));

        log.Record("late-producer-event");

        Assert.Equal(1, log.DroppedFileRecordCount);
        Assert.Contains(log.Snapshot(), line => line.Contains("category=late-producer-event", StringComparison.Ordinal));
    }

    [Fact]
    public void dispose_flushes_queued_records_to_disk()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"dsm-log-{Guid.NewGuid():N}");
        try
        {
            using (var log = new DiagnosticLog(directory, enabled: true))
            {
                log.Record("first");
                log.Record("second", new InvalidOperationException("sensitive message"));
            }

            string contents = File.ReadAllText(Path.Combine(directory, "desktop-system-monitor.log"));
            Assert.Contains("category=first", contents, StringComparison.Ordinal);
            Assert.Contains("category=second exception=InvalidOperationException", contents, StringComparison.Ordinal);
            Assert.DoesNotContain("sensitive message", contents, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [Fact]
    public void disabled_log_keeps_bounded_memory_history_without_creating_files()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"dsm-log-{Guid.NewGuid():N}");
        using (var log = new DiagnosticLog(directory, enabled: false))
        {
            for (int index = 0; index < 205; index++)
            {
                log.Record($"event-{index}");
            }
            Assert.Equal(200, log.Snapshot().Count);
        }
        Assert.False(Directory.Exists(directory));
    }

    [Fact]
    public async Task full_current_file_is_rotated_before_append()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"dsm-log-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        string current = Path.Combine(directory, "desktop-system-monitor.log");
        try
        {
            File.WriteAllBytes(current, new byte[1_000_000]);
            using (var log = new DiagnosticLog(directory, enabled: true))
            {
                log.Record("after-rotation");
                await log.CompleteAsync(TimeSpan.FromSeconds(10));
            }

            Assert.True(File.Exists(current + ".1"));
            Assert.Contains("category=after-rotation", File.ReadAllText(current), StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
