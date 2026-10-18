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

    private sealed class Fake : ISensorHostConnection
    {
        internal bool WrongSequence, Hang, Disposed;
        internal long LastSequence;
        public async ValueTask<SensorHostMessage?> ExchangeAsync(SensorHostMessage request, CancellationToken cancellationToken)
        {
            LastSequence = request.Sequence;
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
