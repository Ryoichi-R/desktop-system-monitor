using System.Diagnostics;
using DesktopSystemMonitor.Core.Platform;
using Xunit;

namespace DesktopSystemMonitor.Mac.Tests;

public sealed class MacSensorSessionTests
{
    [Fact]
    public async Task Default_Session_Fails_Safe_When_Not_Launched_From_A_Bundle()
    {
        await using var session = new MacSensorSession();

        SensorHostMetrics metrics = await session.SampleAsync(default);

        Assert.Equal(SensorHostMetricStatus.Unavailable, metrics.MemoryUtilizationPercent.Status);
    }

    [Fact]
    public async Task Accepts_Matching_Response_And_Increments_Sequence()
    {
        var connection = new Fake();
        await using var session = new MacSensorSession(() => connection, Stopwatch.GetTimestamp, TimeSpan.FromSeconds(1));
        Assert.Equal(25, (await session.SampleAsync(default)).MemoryUtilizationPercent.Value);
        await session.SampleAsync(default);
        Assert.Equal(2, connection.LastSequence);
    }

    [Fact]
    public async Task Wrong_Request_Id_Disconnects_And_Backoff_Prevents_Immediate_Retry()
    {
        int starts = 0;
        var connection = new Fake { WrongSequence = true };
        await using var session = new MacSensorSession(() => { starts++; return connection; }, () => 100, TimeSpan.FromSeconds(1));
        Assert.Equal(SensorHostMetricStatus.Unavailable, (await session.SampleAsync(default)).MemoryUtilizationPercent.Status);
        await session.SampleAsync(default);
        Assert.Equal(1, starts);
        Assert.True(connection.Disposed);
    }

    [Fact]
    public async Task Repeated_Start_Failure_Stops_After_Five_Attempts()
    {
        long clock = 0;
        int starts = 0;
        await using var session = new MacSensorSession(() => { starts++; throw new IOException("crash"); }, () => clock, TimeSpan.FromSeconds(1));
        for (int i = 0; i < 8; i++)
        {
            clock += Stopwatch.Frequency * 60;
            await session.SampleAsync(default);
        }
        Assert.Equal(5, starts);
    }

    [Fact]
    public async Task Timeout_Disconnects_And_Returns_Unavailable()
    {
        var connection = new Fake { Hang = true };
        await using var session = new MacSensorSession(() => connection, Stopwatch.GetTimestamp, TimeSpan.FromMilliseconds(30));
        var metrics = await session.SampleAsync(default);
        Assert.Equal(SensorHostMetricStatus.Unavailable, metrics.MemoryUtilizationPercent.Status);
        Assert.True(connection.Disposed);
    }

    [Fact]
    public async Task Dispose_Cancels_InFlight_Request()
    {
        var connection = new Fake { Hang = true };
        var session = new MacSensorSession(() => connection, Stopwatch.GetTimestamp, TimeSpan.FromSeconds(10));
        Task sample = session.SampleAsync(default).AsTask();
        await session.DisposeAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => sample);
        Assert.True(connection.Disposed);
        await session.DisposeAsync();
    }

    [Fact]
    public async Task Manual_Reconnect_Releases_Stopped_Session_And_Resets_Backoff()
    {
        long clock = 0;
        var connection = new Fake();
        int starts = 0;
        await using var session = new MacSensorSession(() =>
        {
            if (++starts <= 5) throw new IOException("test");
            return connection;
        }, () => clock, TimeSpan.FromSeconds(1));
        for (int i = 0; i < 5; i++)
        {
            clock += 20 * Stopwatch.Frequency;
            await session.SampleAsync(default);
        }
        Assert.Equal(new(MacSensorConnectionState.RetryStopped, MacSensorFailure.ConnectionFailed, 5), session.Status);
        await session.ReconnectAsync();
        Assert.Equal(MacSensorConnectionState.Connecting, session.Status.State);
        Assert.Equal(25, (await session.SampleAsync(default, true, true)).MemoryUtilizationPercent.Value);
        Assert.Equal(MacSensorConnectionState.Connected, session.Status.State);
        Assert.True(connection.IncludedProcesses);
        Assert.True(connection.ResetProcesses);
    }

    [Fact]
    public async Task Reconnect_Cancels_Exchange_And_Reclaims_Host_Before_Starting_Next_Generation()
    {
        var old = new Fake { Hang = true };
        var next = new Fake();
        int starts = 0;
        await using var session = new MacSensorSession(() =>
        {
            if (++starts == 1) return old;
            Assert.True(old.Disposed);
            return next;
        }, Stopwatch.GetTimestamp, TimeSpan.FromSeconds(10));
        var pending = session.SampleAsync(default).AsTask();
        await session.ReconnectAsync();
        Assert.Equal(SensorHostMetricStatus.Unavailable, (await pending).MemoryUtilizationPercent.Status);
        Assert.Equal(25, (await session.SampleAsync(default)).MemoryUtilizationPercent.Value);
        Assert.True(next.LastGeneration > old.LastGeneration);
        Assert.Equal(2, starts);
    }

    private sealed class Fake : ISensorHostConnection
    {
        internal bool WrongSequence, Hang, Disposed;
        internal long LastSequence, LastGeneration;
        internal bool IncludedProcesses, ResetProcesses;
        public async ValueTask<SensorHostMessage?> ExchangeAsync(SensorHostMessage request, CancellationToken cancellationToken)
        {
            LastSequence = request.Sequence;
            LastGeneration = request.HostGeneration;
            IncludedProcesses = request.IncludeHighLoadProcesses;
            ResetProcesses = request.ResetHighLoadProcesses;
            if (Hang) await Task.Delay(Timeout.Infinite, cancellationToken);
            return SensorHostProtocol.Unavailable(request.Sequence + (WrongSequence ? 1 : 0), request.HostGeneration, "") with
            {
                Status = "ok",
                ErrorCode = null,
                Metrics = SensorHostMetrics.Empty with { MemoryUtilizationPercent = SensorHostMetricValue.Ok(25, Stopwatch.GetTimestamp()) },
                GeneratedAtMonotonicTicks = Stopwatch.GetTimestamp(),
            };
        }
        public void Abort() { }
        public ValueTask DisposeAsync() { Disposed = true; return ValueTask.CompletedTask; }
    }
}
