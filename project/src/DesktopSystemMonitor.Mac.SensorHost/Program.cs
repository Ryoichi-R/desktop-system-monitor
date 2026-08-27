using DesktopSystemMonitor.Core.Platform;

namespace DesktopSystemMonitor.Mac.SensorHost;

internal static class Program
{
    public static Task<int> Main() => RunAsync(
        Console.OpenStandardInput(),
        Console.OpenStandardOutput(),
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
            return await SensorHostServer.RunAsync(input, output, lifetime.Token).ConfigureAwait(false);
        }
        finally
        {
            Console.CancelKeyPress -= handler;
        }
    }
}

internal static class SensorHostServer
{
    internal static async Task<int> RunAsync(Stream input, Stream output, CancellationToken cancellationToken)
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
                    ? SensorHostProtocol.Unavailable(request.Sequence, "native-metrics-not-implemented")
                    : SensorHostProtocol.Unavailable(request.Sequence, "unsupported-request");
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
