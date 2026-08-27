using System.Buffers.Binary;

using DesktopSystemMonitor.Core.Platform;

using Xunit;

namespace DesktopSystemMonitor.Core.Tests;

public sealed class SensorHostProtocolTests
{
    [Fact]
    public async Task RoundTrip_Preserves_Fixed_Schema_Message()
    {
        var message = new SensorHostMessage
        {
            Version = SensorHostProtocol.CurrentVersion,
            Kind = "response",
            Sequence = 4,
            Status = "ok",
            Metrics = new SensorHostMetrics
            {
                CpuUtilizationPercent = 25,
                MemoryUtilizationPercent = 50,
                GpuUtilizationPercent = 75,
                DiskReadBytesPerSecond = 1,
                DiskWriteBytesPerSecond = 2,
                NetworkReceiveBytesPerSecond = 3,
                NetworkSendBytesPerSecond = 4,
            },
        };
        await using var stream = new MemoryStream();

        await SensorHostProtocol.WriteAsync(stream, message, CancellationToken.None);
        stream.Position = 0;
        SensorHostMessage actual = (await SensorHostProtocol.ReadAsync(stream, CancellationToken.None))!;

        Assert.Equal(message, actual);
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
        var message = new SensorHostMessage
        {
            Version = SensorHostProtocol.CurrentVersion,
            Kind = "response",
            Sequence = 1,
            Status = "ok",
            Metrics = new SensorHostMetrics { CpuUtilizationPercent = double.NaN },
        };

        Assert.Throws<InvalidDataException>(() => SensorHostProtocol.Validate(message));
    }

    [Fact]
    public void Unavailable_Response_Uses_Stable_Error_Contract()
    {
        SensorHostMessage response = SensorHostProtocol.Unavailable(9, "timeout");

        Assert.Equal(SensorHostProtocol.CurrentVersion, response.Version);
        Assert.Equal("response", response.Kind);
        Assert.Equal("unavailable", response.Status);
        Assert.Equal("timeout", response.ErrorCode);
        Assert.Equal(SensorHostMetrics.Empty, response.Metrics);
    }
}
