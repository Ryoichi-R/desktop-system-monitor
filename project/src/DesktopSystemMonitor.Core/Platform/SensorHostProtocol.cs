using System.Buffers.Binary;
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DesktopSystemMonitor.Core.Platform;

/// <summary>
/// SensorHostとの通信契約。JSON本文を4バイトlittle-endian長で包み、stdoutには
/// プロトコルフレーム以外を出さない。native値はこの境界の両側で再検証する。
/// </summary>
public static class SensorHostProtocol
{
    public const int CurrentVersion = 2;
    public const int MaxPayloadBytes = 64 * 1024;
    public const int MaxNetworkInterfaces = 64;
    public const int MaxProcesses = 32;
    public const int MaxInterfaceNameLength = 128;
    public const int MaxProcessNameLength = 256;

    private static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();

    public static async ValueTask<SensorHostMessage?> ReadAsync(
        Stream input,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(input);
        byte[] header = new byte[sizeof(int)];
        int headerBytes = await ReadAtMostAsync(input, header, cancellationToken).ConfigureAwait(false);
        if (headerBytes == 0)
        {
            return null;
        }
        if (headerBytes != header.Length)
        {
            throw new InvalidDataException("SensorHost frame header is truncated.");
        }

        int payloadLength = BinaryPrimitives.ReadInt32LittleEndian(header);
        if (payloadLength is <= 0 or > MaxPayloadBytes)
        {
            throw new InvalidDataException("SensorHost frame length is outside the allowed range.");
        }

        byte[] payload = new byte[payloadLength];
        int payloadBytes = await ReadAtMostAsync(input, payload, cancellationToken).ConfigureAwait(false);
        if (payloadBytes != payloadLength)
        {
            throw new InvalidDataException("SensorHost frame payload is truncated.");
        }

        SensorHostMessage message;
        try
        {
            message = JsonSerializer.Deserialize<SensorHostMessage>(payload, JsonOptions)
                ?? throw new InvalidDataException("SensorHost frame is empty.");
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("SensorHost frame is not valid JSON.", exception);
        }
        Validate(message);
        return message;
    }

    public static async ValueTask WriteAsync(
        Stream output,
        SensorHostMessage message,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(output);
        Validate(message);
        byte[] payload = JsonSerializer.SerializeToUtf8Bytes(message, JsonOptions);
        if (payload.Length > MaxPayloadBytes)
        {
            throw new InvalidDataException("SensorHost frame payload is too large.");
        }

        byte[] header = new byte[sizeof(int)];
        BinaryPrimitives.WriteInt32LittleEndian(header, payload.Length);
        await output.WriteAsync(header.AsMemory(), cancellationToken).ConfigureAwait(false);
        await output.WriteAsync(payload.AsMemory(), cancellationToken).ConfigureAwait(false);
        await output.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    public static void Validate(SensorHostMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);
        if (message.Version != CurrentVersion)
        {
            throw new InvalidDataException("SensorHost protocol version is not supported.");
        }
        if (message.Sequence < 0)
        {
            throw new InvalidDataException("SensorHost request ID must not be negative.");
        }
        if (message.HostGeneration <= 0)
        {
            throw new InvalidDataException("SensorHost generation must be positive.");
        }
        if (message.Kind is not ("sample" or "response"))
        {
            throw new InvalidDataException("SensorHost message kind is not supported.");
        }
        if (message.Status is not ("ok" or "unavailable" or "error"))
        {
            throw new InvalidDataException("SensorHost message status is not supported.");
        }
        if (message.MonotonicFrequency <= 0 || message.GeneratedAtMonotonicTicks <= 0)
        {
            throw new InvalidDataException("SensorHost monotonic clock metadata is invalid.");
        }
        if (message.Metrics is null)
        {
            throw new InvalidDataException("SensorHost message metrics are missing.");
        }

        ValidateMetric(message.Metrics.CpuUtilizationPercent, 0d, 100d, "CPU utilization", message.GeneratedAtMonotonicTicks);
        ValidateMetric(message.Metrics.CpuFrequencyMhz, 0d, double.MaxValue, "CPU frequency", message.GeneratedAtMonotonicTicks);
        ValidateMetric(message.Metrics.MemoryUtilizationPercent, 0d, 100d, "memory utilization", message.GeneratedAtMonotonicTicks);
        ValidateMetric(message.Metrics.MemoryUsedBytes, 0d, double.MaxValue, "memory used", message.GeneratedAtMonotonicTicks);
        ValidateMetric(message.Metrics.MemoryTotalBytes, 0d, double.MaxValue, "memory total", message.GeneratedAtMonotonicTicks);
        ValidateMetric(message.Metrics.DiskUtilizationPercent, 0d, 100d, "disk utilization", message.GeneratedAtMonotonicTicks);
        ValidateMetric(message.Metrics.DiskReadBytesPerSecond, 0d, double.MaxValue, "disk read rate", message.GeneratedAtMonotonicTicks);
        ValidateMetric(message.Metrics.DiskWriteBytesPerSecond, 0d, double.MaxValue, "disk write rate", message.GeneratedAtMonotonicTicks);
        ValidateMetric(message.Metrics.GpuUtilizationPercent, 0d, 100d, "GPU utilization", message.GeneratedAtMonotonicTicks);
        ValidateMetric(message.Metrics.GpuMemoryUsedBytes, 0d, double.MaxValue, "GPU memory used", message.GeneratedAtMonotonicTicks);
        ValidateMetric(message.Metrics.GpuMemoryLimitBytes, 0d, double.MaxValue, "GPU memory limit", message.GeneratedAtMonotonicTicks);
        ValidateMetric(message.Metrics.CpuPowerWatts, 0d, double.MaxValue, "CPU power", message.GeneratedAtMonotonicTicks);
        ValidateMetric(message.Metrics.GpuPowerWatts, 0d, double.MaxValue, "GPU power", message.GeneratedAtMonotonicTicks);
        ValidateMetric(message.Metrics.CpuTemperatureCelsius, 5d, 120d, "CPU temperature", message.GeneratedAtMonotonicTicks);
        ValidateMetric(message.Metrics.GpuTemperatureCelsius, 5d, 120d, "GPU temperature", message.GeneratedAtMonotonicTicks);
        ValidateCollection(message.Metrics.NetworkInterfaces, MaxNetworkInterfaces, "network interfaces", message.GeneratedAtMonotonicTicks);
        ValidateCollection(message.Metrics.HighLoadProcesses, MaxProcesses, "high-load processes", message.GeneratedAtMonotonicTicks);

        foreach (SensorHostNetworkInterfaceReading networkInterface in message.Metrics.NetworkInterfaces.Values)
        {
            if (networkInterface.InterfaceIndex == 0 ||
                string.IsNullOrWhiteSpace(networkInterface.Name) ||
                networkInterface.Name.Length > MaxInterfaceNameLength)
            {
                throw new InvalidDataException("SensorHost network interface identity is invalid.");
            }
            ValidateMetric(networkInterface.ReceiveBytesPerSecond, 0d, double.MaxValue, "network receive rate", message.GeneratedAtMonotonicTicks);
            ValidateMetric(networkInterface.SendBytesPerSecond, 0d, double.MaxValue, "network send rate", message.GeneratedAtMonotonicTicks);
        }

        foreach (SensorHostProcessReading process in message.Metrics.HighLoadProcesses.Values)
        {
            if (process.ProcessId <= 0 ||
                string.IsNullOrWhiteSpace(process.Name) ||
                process.Name.Length > MaxProcessNameLength)
            {
                throw new InvalidDataException("SensorHost process identity is invalid.");
            }
            ValidateMetric(process.CpuUtilizationPercent, 0d, 100d, "process CPU utilization", message.GeneratedAtMonotonicTicks);
            ValidateMetric(process.PrivateBytes, 0d, double.MaxValue, "process private memory", message.GeneratedAtMonotonicTicks);
            ValidateMetric(process.IoReadBytesPerSecond, 0d, double.MaxValue, "process read rate", message.GeneratedAtMonotonicTicks);
            ValidateMetric(process.IoWriteBytesPerSecond, 0d, double.MaxValue, "process write rate", message.GeneratedAtMonotonicTicks);
        }
    }

    public static void ValidateResponse(
        SensorHostMessage message,
        long expectedSequence,
        long expectedHostGeneration,
        long nowMonotonicTicks,
        long expectedMonotonicFrequency,
        TimeSpan maximumAge)
    {
        Validate(message);
        if (message.Kind != "response" ||
            message.Sequence != expectedSequence ||
            message.HostGeneration != expectedHostGeneration)
        {
            throw new InvalidDataException("SensorHost response does not match the outstanding request.");
        }
        if (expectedMonotonicFrequency <= 0 ||
            message.MonotonicFrequency != expectedMonotonicFrequency ||
            maximumAge <= TimeSpan.Zero ||
            nowMonotonicTicks < message.GeneratedAtMonotonicTicks)
        {
            throw new InvalidDataException("SensorHost response age is invalid.");
        }

        double ageSeconds = (nowMonotonicTicks - message.GeneratedAtMonotonicTicks) /
            (double)message.MonotonicFrequency;
        if (!double.IsFinite(ageSeconds) || ageSeconds > maximumAge.TotalSeconds)
        {
            throw new InvalidDataException("SensorHost response is stale.");
        }

        foreach (long sampledAt in EnumerateSampleTicks(message.Metrics))
        {
            double sampleAgeSeconds = (nowMonotonicTicks - sampledAt) / (double)expectedMonotonicFrequency;
            if (!double.IsFinite(sampleAgeSeconds) || sampleAgeSeconds > maximumAge.TotalSeconds)
            {
                throw new InvalidDataException("SensorHost metric sample is stale.");
            }
        }
    }

    public static SensorHostMessage Unavailable(long sequence, long hostGeneration, string errorCode) => new()
    {
        Version = CurrentVersion,
        Kind = "response",
        Sequence = sequence,
        HostGeneration = hostGeneration,
        Status = "unavailable",
        ErrorCode = errorCode,
        MonotonicFrequency = Stopwatch.Frequency,
        GeneratedAtMonotonicTicks = Stopwatch.GetTimestamp(),
        Metrics = SensorHostMetrics.Empty,
    };

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            NumberHandling = JsonNumberHandling.Strict,
            PropertyNameCaseInsensitive = false,
            AllowDuplicateProperties = false,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        };
        options.Converters.Add(new JsonStringEnumConverter<SensorHostMetricStatus>());
        return options;
    }

    private static void ValidateMetric(
        SensorHostMetricValue metric,
        double minimum,
        double maximum,
        string name,
        long generatedAtMonotonicTicks)
    {
        if (metric is null)
        {
            throw new InvalidDataException($"SensorHost {name} metric is missing.");
        }
        if (!Enum.IsDefined(metric.Status) || metric.SampledAtMonotonicTicks < 0 ||
            metric.SampledAtMonotonicTicks > generatedAtMonotonicTicks)
        {
            throw new InvalidDataException($"SensorHost {name} metadata is invalid.");
        }

        double? metricValue = metric.Value;
        bool hasValue = metricValue.HasValue;
        if (metric.Status is SensorHostMetricStatus.Ok or SensorHostMetricStatus.Stale)
        {
            if (!hasValue || !double.IsFinite(metricValue.GetValueOrDefault()) ||
                metricValue.GetValueOrDefault() < minimum || metricValue.GetValueOrDefault() > maximum ||
                metric.SampledAtMonotonicTicks <= 0)
            {
                throw new InvalidDataException($"SensorHost {name} value is outside the allowed range.");
            }
        }
        else if (hasValue)
        {
            throw new InvalidDataException($"SensorHost {name} must not include a value when unavailable or warming up.");
        }
    }

    private static void ValidateCollection<T>(
        SensorHostMetricGroup<T> collection,
        int maximumCount,
        string name,
        long generatedAtMonotonicTicks)
        where T : class
    {
        if (collection is null)
        {
            throw new InvalidDataException($"SensorHost {name} collection is missing.");
        }
        if (!Enum.IsDefined(collection.Status) ||
            collection.SampledAtMonotonicTicks < 0 ||
            collection.SampledAtMonotonicTicks > generatedAtMonotonicTicks ||
            collection.Values is null ||
            collection.Values.Any(value => value is null))
        {
            throw new InvalidDataException($"SensorHost {name} metadata is invalid.");
        }
        if (collection.Values.Length > maximumCount)
        {
            throw new InvalidDataException($"SensorHost {name} exceed the allowed count.");
        }
        if (collection.Status is SensorHostMetricStatus.Ok or SensorHostMetricStatus.Stale)
        {
            if (collection.SampledAtMonotonicTicks <= 0)
            {
                throw new InvalidDataException($"SensorHost {name} timestamp is invalid.");
            }
        }
        else if (collection.Values.Length != 0)
        {
            throw new InvalidDataException($"SensorHost {name} must be empty when unavailable or warming up.");
        }
    }

    private static IEnumerable<long> EnumerateSampleTicks(SensorHostMetrics metrics)
    {
        SensorHostMetricValue[] values =
        [
            metrics.CpuUtilizationPercent,
            metrics.CpuFrequencyMhz,
            metrics.MemoryUtilizationPercent,
            metrics.MemoryUsedBytes,
            metrics.MemoryTotalBytes,
            metrics.DiskUtilizationPercent,
            metrics.DiskReadBytesPerSecond,
            metrics.DiskWriteBytesPerSecond,
            metrics.GpuUtilizationPercent,
            metrics.GpuMemoryUsedBytes,
            metrics.GpuMemoryLimitBytes,
            metrics.CpuPowerWatts,
            metrics.GpuPowerWatts,
            metrics.CpuTemperatureCelsius,
            metrics.GpuTemperatureCelsius,
        ];
        foreach (SensorHostMetricValue value in values)
        {
            if (value.Status is SensorHostMetricStatus.Ok or SensorHostMetricStatus.Stale)
            {
                yield return value.SampledAtMonotonicTicks;
            }
        }

        if (metrics.NetworkInterfaces.Status is SensorHostMetricStatus.Ok or SensorHostMetricStatus.Stale)
        {
            yield return metrics.NetworkInterfaces.SampledAtMonotonicTicks;
        }
        foreach (SensorHostNetworkInterfaceReading networkInterface in metrics.NetworkInterfaces.Values)
        {
            if (networkInterface.ReceiveBytesPerSecond.Status is SensorHostMetricStatus.Ok or SensorHostMetricStatus.Stale)
            {
                yield return networkInterface.ReceiveBytesPerSecond.SampledAtMonotonicTicks;
            }
            if (networkInterface.SendBytesPerSecond.Status is SensorHostMetricStatus.Ok or SensorHostMetricStatus.Stale)
            {
                yield return networkInterface.SendBytesPerSecond.SampledAtMonotonicTicks;
            }
        }

        if (metrics.HighLoadProcesses.Status is SensorHostMetricStatus.Ok or SensorHostMetricStatus.Stale)
        {
            yield return metrics.HighLoadProcesses.SampledAtMonotonicTicks;
        }
        foreach (SensorHostProcessReading process in metrics.HighLoadProcesses.Values)
        {
            SensorHostMetricValue[] processValues =
            [
                process.CpuUtilizationPercent,
                process.PrivateBytes,
                process.IoReadBytesPerSecond,
                process.IoWriteBytesPerSecond,
            ];
            foreach (SensorHostMetricValue value in processValues)
            {
                if (value.Status is SensorHostMetricStatus.Ok or SensorHostMetricStatus.Stale)
                {
                    yield return value.SampledAtMonotonicTicks;
                }
            }
        }
    }

    private static async ValueTask<int> ReadAtMostAsync(
        Stream input,
        Memory<byte> buffer,
        CancellationToken cancellationToken)
    {
        int total = 0;
        while (total < buffer.Length)
        {
            int read = await input.ReadAsync(buffer[total..], cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                break;
            }
            total += read;
        }
        return total;
    }
}

public enum SensorHostMetricStatus
{
    Ok,
    Unavailable,
    WarmingUp,
    Stale,
}

public sealed record SensorHostMessage
{
    public required int Version { get; init; }
    public required string Kind { get; init; }
    public required long Sequence { get; init; }
    public required long HostGeneration { get; init; }
    public required string Status { get; init; }
    public required long MonotonicFrequency { get; init; }
    public required long GeneratedAtMonotonicTicks { get; init; }
    public required SensorHostMetrics Metrics { get; init; }
    public string? ErrorCode { get; init; }
}

public sealed record SensorHostMetricValue
{
    public required SensorHostMetricStatus Status { get; init; }
    public required double? Value { get; init; }
    public required long SampledAtMonotonicTicks { get; init; }

    public static SensorHostMetricValue Ok(double value, long sampledAtMonotonicTicks) => new()
    {
        Status = SensorHostMetricStatus.Ok,
        Value = value,
        SampledAtMonotonicTicks = sampledAtMonotonicTicks,
    };

    public static SensorHostMetricValue Unavailable() => new()
    {
        Status = SensorHostMetricStatus.Unavailable,
        Value = null,
        SampledAtMonotonicTicks = 0,
    };

    public static SensorHostMetricValue WarmingUp() => new()
    {
        Status = SensorHostMetricStatus.WarmingUp,
        Value = null,
        SampledAtMonotonicTicks = 0,
    };
}

public sealed record SensorHostMetricGroup<T>
    where T : class
{
    public required SensorHostMetricStatus Status { get; init; }
    public required T[] Values { get; init; }
    public required long SampledAtMonotonicTicks { get; init; }

}

public static class SensorHostMetricGroups
{
    public static SensorHostMetricGroup<T> Ok<T>(T[] values, long sampledAtMonotonicTicks)
        where T : class => new()
        {
            Status = SensorHostMetricStatus.Ok,
            Values = values,
            SampledAtMonotonicTicks = sampledAtMonotonicTicks,
        };

    public static SensorHostMetricGroup<T> Unavailable<T>()
        where T : class => new()
        {
            Status = SensorHostMetricStatus.Unavailable,
            Values = Array.Empty<T>(),
            SampledAtMonotonicTicks = 0,
        };
}

public sealed record SensorHostNetworkInterfaceReading
{
    public required uint InterfaceIndex { get; init; }
    public required string Name { get; init; }
    public required SensorHostMetricValue ReceiveBytesPerSecond { get; init; }
    public required SensorHostMetricValue SendBytesPerSecond { get; init; }
}

public sealed record SensorHostProcessReading
{
    public required int ProcessId { get; init; }
    public required string Name { get; init; }
    public required SensorHostMetricValue CpuUtilizationPercent { get; init; }
    public required SensorHostMetricValue PrivateBytes { get; init; }
    public required SensorHostMetricValue IoReadBytesPerSecond { get; init; }
    public required SensorHostMetricValue IoWriteBytesPerSecond { get; init; }
}

public sealed record SensorHostMetrics
{
    public required SensorHostMetricValue CpuUtilizationPercent { get; init; }
    public required SensorHostMetricValue CpuFrequencyMhz { get; init; }
    public required SensorHostMetricValue MemoryUtilizationPercent { get; init; }
    public required SensorHostMetricValue MemoryUsedBytes { get; init; }
    public required SensorHostMetricValue MemoryTotalBytes { get; init; }
    public required SensorHostMetricValue DiskUtilizationPercent { get; init; }
    public required SensorHostMetricValue DiskReadBytesPerSecond { get; init; }
    public required SensorHostMetricValue DiskWriteBytesPerSecond { get; init; }
    public required SensorHostMetricGroup<SensorHostNetworkInterfaceReading> NetworkInterfaces { get; init; }
    public required SensorHostMetricValue GpuUtilizationPercent { get; init; }
    public required SensorHostMetricValue GpuMemoryUsedBytes { get; init; }
    public required SensorHostMetricValue GpuMemoryLimitBytes { get; init; }
    public required SensorHostMetricValue CpuPowerWatts { get; init; }
    public required SensorHostMetricValue GpuPowerWatts { get; init; }
    public required SensorHostMetricValue CpuTemperatureCelsius { get; init; }
    public required SensorHostMetricValue GpuTemperatureCelsius { get; init; }
    public required SensorHostMetricGroup<SensorHostProcessReading> HighLoadProcesses { get; init; }

    public static SensorHostMetrics Empty { get; } = new()
    {
        CpuUtilizationPercent = SensorHostMetricValue.Unavailable(),
        CpuFrequencyMhz = SensorHostMetricValue.Unavailable(),
        MemoryUtilizationPercent = SensorHostMetricValue.Unavailable(),
        MemoryUsedBytes = SensorHostMetricValue.Unavailable(),
        MemoryTotalBytes = SensorHostMetricValue.Unavailable(),
        DiskUtilizationPercent = SensorHostMetricValue.Unavailable(),
        DiskReadBytesPerSecond = SensorHostMetricValue.Unavailable(),
        DiskWriteBytesPerSecond = SensorHostMetricValue.Unavailable(),
        NetworkInterfaces = SensorHostMetricGroups.Unavailable<SensorHostNetworkInterfaceReading>(),
        GpuUtilizationPercent = SensorHostMetricValue.Unavailable(),
        GpuMemoryUsedBytes = SensorHostMetricValue.Unavailable(),
        GpuMemoryLimitBytes = SensorHostMetricValue.Unavailable(),
        CpuPowerWatts = SensorHostMetricValue.Unavailable(),
        GpuPowerWatts = SensorHostMetricValue.Unavailable(),
        CpuTemperatureCelsius = SensorHostMetricValue.Unavailable(),
        GpuTemperatureCelsius = SensorHostMetricValue.Unavailable(),
        HighLoadProcesses = SensorHostMetricGroups.Unavailable<SensorHostProcessReading>(),
    };
}
