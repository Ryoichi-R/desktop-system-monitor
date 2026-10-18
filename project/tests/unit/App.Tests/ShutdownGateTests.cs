using System;
using System.Threading;
using System.Threading.Tasks;
using DesktopSystemMonitor.App.Startup;
using Xunit;

namespace DesktopSystemMonitor.App.Tests;

public sealed class ShutdownGateTests
{
    [Fact]
    public void work_is_rejected_after_shutdown_begins()
    {
        var gate = new ShutdownGate();
        gate.BeginShutdown();
        bool ran = false;

        bool accepted = gate.TryRunBeforeShutdown(() => ran = true);

        Assert.False(accepted);
        Assert.False(ran);
        Assert.True(gate.IsShutdownStarted);
    }

    [Fact]
    public async Task begin_shutdown_waits_for_an_already_accepted_submission()
    {
        var gate = new ShutdownGate();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task submission = Task.Factory.StartNew(
            () => gate.TryRunBeforeShutdown(() =>
            {
                entered.SetResult();
                release.Task.GetAwaiter().GetResult();
            }),
            CancellationToken.None,
            TaskCreationOptions.LongRunning,
            TaskScheduler.Default);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));

        var shutdownAttempted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task shutdown = Task.Factory.StartNew(
            () =>
            {
                shutdownAttempted.SetResult();
                gate.BeginShutdown();
            },
            CancellationToken.None,
            TaskCreationOptions.LongRunning,
            TaskScheduler.Default);
        await shutdownAttempted.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.False(shutdown.IsCompleted);
        release.SetResult();
        await Task.WhenAll(submission, shutdown).WaitAsync(TimeSpan.FromSeconds(10));

        Assert.False(gate.TryRunBeforeShutdown(() => { }));
    }
}
