using DesktopSystemMonitor.Core.Metrics;
using DesktopSystemMonitor.Core.Sampling;
using Xunit;

namespace DesktopSystemMonitor.Core.Tests.Sampling;

public class SamplingOrchestratorTests
{
    [Fact]
    public async Task snapshots_are_delivered_at_the_configured_cadence()
    {
        int count = 0;
        var receivedThreeSnapshots = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var orch = new SamplingOrchestrator(
            sample: token => new ValueTask<MetricSnapshot>(MetricSnapshot.Warmup(DateTimeOffset.UtcNow)),
            onSnapshot: _ =>
            {
                if (Interlocked.Increment(ref count) >= 3)
                {
                    receivedThreeSnapshots.TrySetResult(true);
                }
            },
            onError: _ => { },
            interval: TimeSpan.FromMilliseconds(50));
        orch.Start();
        await receivedThreeSnapshots.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(count >= 3, $"Expected >= 3 snapshots, got {count}");
    }

    [Fact]
    public async Task an_in_flight_sample_causes_the_next_tick_to_skip()
    {
        var gate = new TaskCompletionSource();
        int calls = 0;
        var orch = new SamplingOrchestrator(
            sample: async token =>
            {
                Interlocked.Increment(ref calls);
                // Honor cancellation so DisposeAsync can unwind even if the
                // test leaves the gate closed.
                await gate.Task.WaitAsync(token);
                return MetricSnapshot.Warmup(DateTimeOffset.UtcNow);
            },
            onSnapshot: _ => { },
            onError: _ => { },
            interval: TimeSpan.FromMilliseconds(30));
        try
        {
            orch.Start();
            await Task.Delay(200);
            Assert.Equal(1, Volatile.Read(ref calls));
            Assert.True(orch.SkippedTicks >= 3);
        }
        finally
        {
            gate.TrySetResult();
            await orch.DisposeAsync();
        }
    }

    [Fact]
    public async Task sample_exception_is_forwarded_to_onError_and_loop_continues()
    {
        int errors = 0;
        int snapshots = 0;
        bool firstCall = true;
        await using var orch = new SamplingOrchestrator(
            sample: _ =>
            {
                if (firstCall)
                {
                    firstCall = false;
                    throw new InvalidOperationException("boom");
                }
                return new ValueTask<MetricSnapshot>(MetricSnapshot.Warmup(DateTimeOffset.UtcNow));
            },
            onSnapshot: _ => Interlocked.Increment(ref snapshots),
            onError: _ => Interlocked.Increment(ref errors),
            interval: TimeSpan.FromMilliseconds(30));
        orch.Start();
        await Task.Delay(200);
        Assert.True(errors >= 1);
        Assert.True(snapshots >= 1);
    }

    [Fact]
    public async Task pause_stops_new_samples_and_resume_restarts_them()
    {
        int count = 0;
        await using var orch = new SamplingOrchestrator(
            sample: _ => new ValueTask<MetricSnapshot>(MetricSnapshot.Warmup(DateTimeOffset.UtcNow)),
            onSnapshot: _ => Interlocked.Increment(ref count),
            onError: _ => { },
            interval: TimeSpan.FromMilliseconds(20));
        orch.Start();
        await Task.Delay(80);
        orch.Pause();
        Assert.True(orch.IsPaused);
        int pausedAt = Volatile.Read(ref count);
        await Task.Delay(80);
        Assert.InRange(Volatile.Read(ref count), pausedAt, pausedAt + 1);
        orch.Resume();
        Assert.False(orch.IsPaused);
        await Task.Delay(80);
        Assert.True(Volatile.Read(ref count) > pausedAt);
    }

    [Fact]
    public async Task start_is_idempotent()
    {
        int count = 0;
        await using var orch = new SamplingOrchestrator(
            sample: _ => new ValueTask<MetricSnapshot>(MetricSnapshot.Warmup(DateTimeOffset.UtcNow)),
            onSnapshot: _ => Interlocked.Increment(ref count),
            onError: _ => { },
            interval: TimeSpan.FromMilliseconds(25));
        orch.Start();
        orch.Start();
        await Task.Delay(100);
        Assert.InRange(count, 2, 6);
        Assert.True(orch.IsRunning);
    }
}
