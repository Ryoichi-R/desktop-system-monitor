using System;
using System.Threading.Tasks;
using DesktopSystemMonitor.App.Startup;
using DesktopSystemMonitor.Core.Metrics;
using Xunit;

namespace DesktopSystemMonitor.App.Tests;

public sealed class StartupBatteryProbeTests
{
    [Fact]
    public void synchronous_probe_result_is_available_for_initial_layout()
    {
        BatterySnapshot expected = BatterySnapshot.Absent();

        bool completed = StartupBatteryProbe.TryConsume(
            ValueTask.FromResult(expected),
            (_, _) => throw new InvalidOperationException("diagnostic should not be written"),
            out BatterySnapshot actual);

        Assert.True(completed);
        Assert.Same(expected, actual);
    }

    [Fact]
    public async Task asynchronous_probe_does_not_block_and_observes_failure()
    {
        var source = new TaskCompletionSource<BatterySnapshot>(TaskCreationOptions.RunContinuationsAsynchronously);
        var diagnostic = new TaskCompletionSource<(string Category, Exception Error)>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        bool completed = StartupBatteryProbe.TryConsume(
            new ValueTask<BatterySnapshot>(source.Task),
            (category, error) => diagnostic.TrySetResult((category, error)),
            out BatterySnapshot initial);

        Assert.False(completed);
        Assert.Equal(MetricStatus.Unavailable, initial.Status);
        source.SetException(new InvalidOperationException("injected"));
        (string category, Exception error) = await diagnostic.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal("battery-startup-probe-failure", category);
        Assert.IsType<InvalidOperationException>(error);
    }

    [Fact]
    public async Task observer_completes_when_diagnostic_sink_throws()
    {
        await StartupBatteryProbe.ObserveAsync(
            Task.FromException<BatterySnapshot>(new InvalidOperationException("injected")),
            (_, _) => throw new ObjectDisposedException("diagnostic"));
    }
}
