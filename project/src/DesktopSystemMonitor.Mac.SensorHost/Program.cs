using DesktopSystemMonitor.Core.Platform;

namespace DesktopSystemMonitor.Mac.SensorHost;

internal static class Program
{
    internal static Func<Stream> StandardInputFactory { get; set; } = Console.OpenStandardInput;
    internal static Func<Stream> StandardOutputFactory { get; set; } = Console.OpenStandardOutput;

    internal static Func<Func<SensorHostMetrics>?> SamplerFactory { get; set; } = () =>
        OperatingSystem.IsMacOS() ? new NativeMetricCollector().Sample : null;

    public static Task<int> Main() => RunAsync(
        StandardInputFactory(),
        StandardOutputFactory(),
        CancellationToken.None);

    internal static async Task<int> RunAsync(Stream input, Stream output, CancellationToken cancellationToken)
    {
        using CancellationTokenSource lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        ConsoleCancelEventHandler handler = (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            lifetime.Cancel();
        };
        Console.CancelKeyPress += handler;
        try
        {
            var sample = SamplerFactory();
            using var owner = sample?.Target as IDisposable;
            return await SensorHostServer.RunAsync(input, output, lifetime.Token, sample).ConfigureAwait(false);
        }
        finally
        {
            Console.CancelKeyPress -= handler;
        }
    }
}

internal static class SensorHostServer
{
    internal static async Task<int> RunAsync(Stream input, Stream output, CancellationToken cancellationToken, Func<SensorHostMetrics>? sample = null)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                SensorHostMessage? request = await SensorHostProtocol.ReadAsync(input, cancellationToken).ConfigureAwait(false);
                if (request is null)
                {
                    return 0;
                }

                SensorHostMessage response = request.Kind == "sample"
                    ? SensorHostProtocol.Unavailable(request.Sequence, request.HostGeneration, "native-metrics-not-implemented")
                    : SensorHostProtocol.Unavailable(request.Sequence, request.HostGeneration, "unsupported-request");
                if (request.Kind == "sample" && sample is not null)
                {
                    SensorHostMetrics metrics = sample();
                    response = response with { Status = "ok", ErrorCode = null, Metrics = metrics, GeneratedAtMonotonicTicks = System.Diagnostics.Stopwatch.GetTimestamp() };
                }
                await SensorHostProtocol.WriteAsync(output, response, cancellationToken).ConfigureAwait(false);
            }

            return 0;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return 0;
        }
        catch (InvalidDataException)
        {
            return 2;
        }
    }
}
