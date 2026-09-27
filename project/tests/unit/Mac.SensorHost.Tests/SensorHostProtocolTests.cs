using System.Buffers.Binary;
using System.Diagnostics;
using System.Text.Json;

using DesktopSystemMonitor.Core.Platform;

using Xunit;

namespace DesktopSystemMonitor.Mac.SensorHost.Tests;

public sealed class SensorHostProtocolTests
{
    [Fact]
    public async Task RoundTrip_Preserves_Fixed_Metric_Contract()
    {
        SensorHostMessage message = new()
        {
            Version = SensorHostProtocol.CurrentVersion,
            Kind = "response",
            Sequence = 4,
            HostGeneration = 2,
            Status = "unavailable",
            MonotonicFrequency = Stopwatch.Frequency,
            GeneratedAtMonotonicTicks = Stopwatch.GetTimestamp(),
            Metrics = SensorHostMetrics.Empty,
        };
        await using var stream = new MemoryStream();

        await SensorHostProtocol.WriteAsync(stream, message, CancellationToken.None);
        stream.Position = 0;
        SensorHostMessage actual = (await SensorHostProtocol.ReadAsync(stream, CancellationToken.None))!;

        Assert.Equal(JsonSerializer.Serialize(message), JsonSerializer.Serialize(actual));
        Assert.Equal(SensorHostMetricStatus.Unavailable, actual.Metrics.CpuUtilizationPercent.Status);
        Assert.Null(actual.Metrics.CpuUtilizationPercent.Value);
        Assert.Empty(actual.Metrics.HighLoadProcesses.Values);
    }

    [Fact]
    public async Task Read_Rejects_OverSized_Frame_Before_Allocating_Payload()
    {
        await using var stream = new MemoryStream();
        byte[] header = new byte[sizeof(int)];
        BinaryPrimitives.WriteInt32LittleEndian(header, SensorHostProtocol.MaxPayloadBytes + 1);
        await stream.WriteAsync(header);
        stream.Position = 0;

        await Assert.ThrowsAsync<InvalidDataException>(async () =>
            await SensorHostProtocol.ReadAsync(stream, CancellationToken.None));
    }

    [Fact]
    public async Task Read_Rejects_Truncated_And_Malformed_Frames()
    {
        await using var truncated = new MemoryStream();
        byte[] truncatedHeader = new byte[sizeof(int)];
        BinaryPrimitives.WriteInt32LittleEndian(truncatedHeader, 4);
        await truncated.WriteAsync(truncatedHeader);
        await truncated.WriteAsync(new byte[] { (byte)'{' });
        truncated.Position = 0;

        await Assert.ThrowsAsync<InvalidDataException>(async () =>
            await SensorHostProtocol.ReadAsync(truncated, CancellationToken.None));

        await using var malformed = new MemoryStream();
        byte[] malformedPayload = "{}"u8.ToArray();
        byte[] malformedHeader = new byte[sizeof(int)];
        BinaryPrimitives.WriteInt32LittleEndian(malformedHeader, malformedPayload.Length);
        await malformed.WriteAsync(malformedHeader);
        await malformed.WriteAsync(malformedPayload);
        malformed.Position = 0;

        await Assert.ThrowsAsync<InvalidDataException>(async () =>
            await SensorHostProtocol.ReadAsync(malformed, CancellationToken.None));
    }

    [Fact]
    public void Validate_Rejects_NonFinite_Ok_Metric()
    {
        SensorHostMetrics metrics = SensorHostMetrics.Empty with
        {
            CpuUtilizationPercent = SensorHostMetricValue.Ok(double.NaN, 10),
        };
        SensorHostMessage message = new()
        {
            Version = SensorHostProtocol.CurrentVersion,
            Kind = "response",
            Sequence = 1,
            HostGeneration = 1,
            Status = "ok",
            MonotonicFrequency = Stopwatch.Frequency,
            GeneratedAtMonotonicTicks = Stopwatch.GetTimestamp(),
            Metrics = metrics,
        };

        Assert.Throws<InvalidDataException>(() => SensorHostProtocol.Validate(message));
    }

    [Fact]
    public void Unavailable_Response_Uses_Stable_Error_And_Generation_Contract()
    {
        SensorHostMessage response = SensorHostProtocol.Unavailable(9, 3, "timeout");

        Assert.Equal(SensorHostProtocol.CurrentVersion, response.Version);
        Assert.Equal("response", response.Kind);
        Assert.Equal(3, response.HostGeneration);
        Assert.Equal("unavailable", response.Status);
        Assert.Equal("timeout", response.ErrorCode);
        Assert.Equal(SensorHostMetrics.Empty, response.Metrics);
    }
}
