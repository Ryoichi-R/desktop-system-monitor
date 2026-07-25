using System.Diagnostics;
using DesktopSystemMonitor.Windows.Processes;
using Xunit;

namespace DesktopSystemMonitor.Windows.Tests.Processes;

public sealed class ProcessLoadSamplerTests
{
    [Fact]
    public void samples_current_process_and_reuses_pid_start_time_baseline()
    {
        var time = new SequenceTimeProvider(
            DateTimeOffset.UtcNow,
            [0, 10, 1_000, 1_010]);
        using var sampler = new ProcessLoadSampler(
            () => [Process.GetCurrentProcess()],
            time,
            1,
            new RecordingDiagnostics(),
            new StubPrivateWorkingSetReader(64 * 1024));

        IReadOnlyList<ProcessLoadRow> first = sampler.Sample();
        time.Advance(TimeSpan.FromSeconds(1));
        IReadOnlyList<ProcessLoadRow> second = sampler.Sample();

        Assert.Single(first);
        ProcessLoadRow row = Assert.Single(second);
        Assert.True(double.IsFinite(row.CpuPercent));
        Assert.Equal(64 * 1024, row.PrivateWorkingSetBytes);
    }

    [Fact]
    public void backs_off_after_budget_overrun_and_recovers_after_three_fast_cycles()
    {
        var time = new SequenceTimeProvider(
            DateTimeOffset.UtcNow,
            [0, 300, 1_000, 1_100, 2_000, 2_100, 3_000, 3_100]);
        var diagnostics = new RecordingDiagnostics();
        using var sampler = new ProcessLoadSampler(
            () => [],
            time,
            1,
            diagnostics);

        sampler.Sample();
        Assert.Equal(TimeSpan.FromSeconds(5), sampler.NextInterval);

        sampler.Sample();
        sampler.Sample();
        sampler.Sample();

        Assert.Equal(TimeSpan.FromSeconds(2), sampler.NextInterval);
        Assert.Contains(diagnostics.Events, item => item.EventCode == ProcessLoadDiagnosticEvent.BudgetExceeded);
        Assert.Contains(diagnostics.Events, item => item.EventCode == ProcessLoadDiagnosticEvent.IntervalRecovered);
    }

    [Fact]
    public void cancellation_is_checked_before_process_enumeration()
    {
        bool enumerated = false;
        using var sampler = new ProcessLoadSampler(
            () =>
            {
                enumerated = true;
                return [];
            },
            new SequenceTimeProvider(DateTimeOffset.UtcNow, []),
            1,
            new RecordingDiagnostics());
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Assert.Throws<OperationCanceledException>(() => sampler.Sample(cancellationToken: cancellation.Token));
        Assert.False(enumerated);
    }

    [Fact]
    public void reset_restores_the_normal_interval()
    {
        var time = new SequenceTimeProvider(DateTimeOffset.UtcNow, [0, 300]);
        using var sampler = new ProcessLoadSampler(
            () => [],
            time,
            1,
            new RecordingDiagnostics());
        sampler.Sample();

        sampler.Reset();

        Assert.Equal(TimeSpan.FromSeconds(2), sampler.NextInterval);
    }

    [Fact]
    public void missing_private_working_set_keeps_the_process_row()
    {
        var time = new SequenceTimeProvider(DateTimeOffset.UtcNow, [0, 10]);
        using var sampler = new ProcessLoadSampler(
            () => [Process.GetCurrentProcess()],
            time,
            1,
            new RecordingDiagnostics(),
            new StubPrivateWorkingSetReader(null));

        ProcessLoadRow row = Assert.Single(sampler.Sample(ProcessLoadSort.Memory));

        Assert.Null(row.PrivateWorkingSetBytes);
    }

    private sealed class SequenceTimeProvider(
        DateTimeOffset utcNow,
        IEnumerable<long> timestamps) : TimeProvider
    {
        private readonly Queue<long> _timestamps = new(timestamps);
        private DateTimeOffset _utcNow = utcNow;

        public override long TimestampFrequency => 1_000;
        public override DateTimeOffset GetUtcNow() => _utcNow;
        public override long GetTimestamp() => _timestamps.Dequeue();
        public void Advance(TimeSpan elapsed) => _utcNow += elapsed;
    }

    private sealed class RecordingDiagnostics : IProcessLoadDiagnostics
    {
        public List<(ProcessLoadDiagnosticEvent EventCode, int Count, TimeSpan Elapsed)> Events { get; } = [];

        public void Record(ProcessLoadDiagnosticEvent eventCode, int processCount, TimeSpan elapsed) =>
            Events.Add((eventCode, processCount, elapsed));
    }

    private sealed class StubPrivateWorkingSetReader(long? value) : IPrivateWorkingSetReader
    {
        public void BeginSample()
        {
        }

        public bool TryGetBytes(Process process, out long bytes)
        {
            bytes = value.GetValueOrDefault();
            return value.HasValue;
        }

        public void Dispose()
        {
        }
    }
}
