using DesktopSystemMonitor.Core.Metrics;
using DesktopSystemMonitor.Core.Sampling;
using DesktopSystemMonitor.Core.Utility;
using Xunit;

namespace DesktopSystemMonitor.Core.Tests.Sampling;

public sealed class MetricSnapshotSamplerTests
{
    [Fact]
    public async Task one_source_failure_does_not_suppress_other_metrics()
    {
        var errors = new List<string>();
        var sampler = new MetricSnapshotSampler(
            new ThrowingSource<CpuSnapshot>(),
            new ConstantSource<MemorySnapshot>(MemorySnapshot.Warmup()),
            new ConstantSource<GpuSnapshot>(GpuSnapshot.Warmup()),
            new ConstantSource<NetworkSnapshot>(NetworkSnapshot.Warmup()),
            new ConstantSource<PowerSnapshot>(PowerSnapshot.Warmup()),
            new FakeClock(),
            (source, _) => errors.Add(source));

        MetricSnapshot snapshot = await sampler.SampleAsync(default);

        Assert.Equal(MetricStatus.Unavailable, snapshot.Cpu.UtilizationStatus);
        Assert.Equal(MetricStatus.WarmingUp, snapshot.Memory.Status);
        Assert.Equal(MetricStatus.WarmingUp, snapshot.Gpu.OverallStatus);
        Assert.Equal(MetricStatus.WarmingUp, snapshot.Network.AggregateStatus);
        Assert.Equal(["cpu-sample-failure"], errors);
    }

    [Fact]
    public async Task failures_in_all_sources_are_reported_independently()
    {
        var errors = new List<string>();
        var sampler = new MetricSnapshotSampler(
            new ThrowingSource<CpuSnapshot>(),
            new ThrowingSource<MemorySnapshot>(),
            new ThrowingSource<GpuSnapshot>(),
            new ThrowingSource<NetworkSnapshot>(),
            new ThrowingSource<PowerSnapshot>(),
            new FakeClock(),
            (source, _) => errors.Add(source));

        MetricSnapshot snapshot = await sampler.SampleAsync(default);

        Assert.Equal(
            ["cpu-sample-failure", "memory-sample-failure", "gpu-sample-failure", "network-sample-failure", "power-sample-failure"],
            errors);
        Assert.Equal(MetricStatus.Unavailable, snapshot.Cpu.UtilizationStatus);
        Assert.Equal(MetricStatus.Unavailable, snapshot.Memory.Status);
        Assert.Equal(MetricStatus.Unavailable, snapshot.Gpu.OverallStatus);
        Assert.Equal(MetricStatus.Unavailable, snapshot.Network.AggregateStatus);
        Assert.Equal(MetricStatus.Unavailable, snapshot.Power.CpuPackageStatus);
    }

    [Fact]
    public async Task caller_cancellation_is_not_converted_to_unavailable()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var sampler = new MetricSnapshotSampler(
            new CanceledSource<CpuSnapshot>(),
            new ConstantSource<MemorySnapshot>(MemorySnapshot.Warmup()),
            new ConstantSource<GpuSnapshot>(GpuSnapshot.Warmup()),
            new ConstantSource<NetworkSnapshot>(NetworkSnapshot.Warmup()),
            new ConstantSource<PowerSnapshot>(PowerSnapshot.Warmup()),
            new FakeClock(),
            (_, _) => { });
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await sampler.SampleAsync(cts.Token));
    }

    [Fact]
    public async Task legacy_constructor_keeps_power_unavailable()
    {
        var sampler = new MetricSnapshotSampler(
            new ConstantSource<CpuSnapshot>(CpuSnapshot.Warmup()),
            new ConstantSource<MemorySnapshot>(MemorySnapshot.Warmup()),
            new ConstantSource<GpuSnapshot>(GpuSnapshot.Warmup()),
            new ConstantSource<NetworkSnapshot>(NetworkSnapshot.Warmup()),
            new FakeClock(),
            (_, _) => { });

        MetricSnapshot snapshot = await sampler.SampleAsync(default);

        Assert.Equal(MetricStatus.Unavailable, snapshot.Power.CpuPackageStatus);
    }

    [Fact]
    public async Task baseline_reset_requests_are_coalesced_and_consumed_before_sampling()
    {
        var cpu = new CountingSource<CpuSnapshot>(CpuSnapshot.Warmup());
        var memory = new CountingSource<MemorySnapshot>(MemorySnapshot.Warmup());
        var gpu = new CountingSource<GpuSnapshot>(GpuSnapshot.Warmup());
        var network = new CountingSource<NetworkSnapshot>(NetworkSnapshot.Warmup());
        var power = new CountingSource<PowerSnapshot>(PowerSnapshot.Warmup());
        var sampler = new MetricSnapshotSampler(cpu, memory, gpu, network, power, new FakeClock(), (_, _) => { });

        sampler.RequestBaselineReset();
        sampler.RequestBaselineReset();
        _ = await sampler.SampleAsync(default);
        _ = await sampler.SampleAsync(default);

        Assert.Equal(1, cpu.ResetCalls);
        Assert.Equal(1, memory.ResetCalls);
        Assert.Equal(1, gpu.ResetCalls);
        Assert.Equal(1, network.ResetCalls);
        Assert.Equal(1, power.ResetCalls);
        Assert.Equal(["reset", "sample", "sample"], cpu.Operations);
    }

    [Fact]
    public async Task reset_failure_is_isolated_from_other_sources_and_sampling()
    {
        var errors = new List<string>();
        var cpu = new ThrowingResetSource<CpuSnapshot>(CpuSnapshot.Warmup());
        var memory = new CountingSource<MemorySnapshot>(MemorySnapshot.Warmup());
        var sampler = new MetricSnapshotSampler(
            cpu,
            memory,
            new CountingSource<GpuSnapshot>(GpuSnapshot.Warmup()),
            new CountingSource<NetworkSnapshot>(NetworkSnapshot.Warmup()),
            new CountingSource<PowerSnapshot>(PowerSnapshot.Warmup()),
            new FakeClock(),
            (source, _) => errors.Add(source));

        sampler.RequestBaselineReset();
        MetricSnapshot snapshot = await sampler.SampleAsync(default);

        Assert.Equal(["cpu-reset-failure"], errors);
        Assert.Equal(1, memory.ResetCalls);
        Assert.Equal(MetricStatus.WarmingUp, snapshot.Cpu.UtilizationStatus);
    }

    [Fact]
    public async Task reset_requested_during_sample_waits_for_the_next_sample_boundary()
    {
        var cpu = new BlockingCpuSource();
        var sampler = new MetricSnapshotSampler(
            cpu,
            new ConstantSource<MemorySnapshot>(MemorySnapshot.Warmup()),
            new ConstantSource<GpuSnapshot>(GpuSnapshot.Warmup()),
            new ConstantSource<NetworkSnapshot>(NetworkSnapshot.Warmup()),
            new ConstantSource<PowerSnapshot>(PowerSnapshot.Warmup()),
            new FakeClock(),
            (_, _) => { });

        ValueTask<MetricSnapshot> first = sampler.SampleAsync(default);
        await cpu.SampleStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        sampler.RequestBaselineReset();
        Assert.Equal(0, cpu.ResetCalls);

        cpu.ReleaseSample.SetResult();
        _ = await first;
        _ = await sampler.SampleAsync(default);

        Assert.Equal(1, cpu.ResetCalls);
    }

    private sealed class ConstantSource<T>(T value) : IMetricSource<T>
    {
        public ValueTask<T> SampleAsync(CancellationToken cancellationToken) => ValueTask.FromResult(value);
        public void ResetBaseline() { }
        public void Dispose() { }
    }

    private sealed class ThrowingSource<T> : IMetricSource<T>
    {
        public ValueTask<T> SampleAsync(CancellationToken cancellationToken) => throw new InvalidOperationException("test");
        public void ResetBaseline() { }
        public void Dispose() { }
    }

    private sealed class CanceledSource<T> : IMetricSource<T>
    {
        public ValueTask<T> SampleAsync(CancellationToken cancellationToken) => ValueTask.FromCanceled<T>(cancellationToken);
        public void ResetBaseline() { }
        public void Dispose() { }
    }

    private sealed class CountingSource<T>(T value) : IMetricSource<T>
    {
        public List<string> Operations { get; } = [];
        public int ResetCalls { get; private set; }
        public ValueTask<T> SampleAsync(CancellationToken cancellationToken)
        {
            Operations.Add("sample");
            return ValueTask.FromResult(value);
        }
        public void ResetBaseline()
        {
            ResetCalls++;
            Operations.Add("reset");
        }
        public void Dispose() { }
    }

    private sealed class ThrowingResetSource<T>(T value) : IMetricSource<T>
    {
        public ValueTask<T> SampleAsync(CancellationToken cancellationToken) => ValueTask.FromResult(value);
        public void ResetBaseline() => throw new InvalidOperationException("reset test");
        public void Dispose() { }
    }

    private sealed class BlockingCpuSource : IMetricSource<CpuSnapshot>
    {
        private int _sampleCalls;
        public TaskCompletionSource SampleStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ReleaseSample { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int ResetCalls { get; private set; }

        public async ValueTask<CpuSnapshot> SampleAsync(CancellationToken cancellationToken)
        {
            if (Interlocked.Increment(ref _sampleCalls) == 1)
            {
                SampleStarted.SetResult();
                await ReleaseSample.Task.WaitAsync(cancellationToken);
            }
            return CpuSnapshot.Warmup();
        }

        public void ResetBaseline() => ResetCalls++;
        public void Dispose() { }
    }

    private sealed class FakeClock : IClock
    {
        public DateTimeOffset UtcNow { get; } = new(2026, 7, 11, 0, 0, 0, TimeSpan.Zero);
        public long GetTimestampTicks() => 0;
        public double TicksToSeconds(long deltaTicks) => 0;
    }
}
