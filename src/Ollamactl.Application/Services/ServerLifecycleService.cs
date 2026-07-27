using System.Diagnostics;
using System.Net;
using Ollamactl.Application.Abstractions;
using Ollamactl.Application.Configuration;
using Ollamactl.Application.Exceptions;
using Ollamactl.Application.Server;

namespace Ollamactl.Application.Services;

public sealed class ServerLifecycleService(
    IOllamaApiClientFactory clientFactory,
    IOllamaServerManager serverManager)
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(200);

    public async Task<ServerLaunchResult> StartAsync(
        ResolvedExecutionContext context,
        string ollamaExecutable,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentException.ThrowIfNullOrWhiteSpace(ollamaExecutable);

        if (!IsLocalEndpoint(context.Configuration.Endpoint))
        {
            throw new InvalidOperationException(
                "A local Ollama process cannot be started for a remote --host endpoint.");
        }

        var existingVersion = await TryGetVersionAsync(context.Configuration, cancellationToken)
            .ConfigureAwait(false);
        if (existingVersion is not null)
        {
            return new ServerLaunchResult(
                StartedByOllamactl: false,
                AlreadyAvailable: true,
                Process: null,
                existingVersion,
                ReadinessMilliseconds: 0);
        }

        var process = await serverManager.StartAsync(
                new ServerStartOptions(
                    context.Configuration.Paths.ConfigDirectory,
                    ollamaExecutable,
                    context.EffectiveEnvironment),
                cancellationToken)
            .ConfigureAwait(false);

        var stopwatch = Stopwatch.StartNew();
        using var startupCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        startupCancellation.CancelAfter(context.Configuration.Timeout);

        try
        {
            while (true)
            {
                startupCancellation.Token.ThrowIfCancellationRequested();
                var version = await TryGetVersionAsync(
                        context.Configuration,
                        startupCancellation.Token)
                    .ConfigureAwait(false);
                if (version is not null)
                {
                    stopwatch.Stop();
                    return new ServerLaunchResult(
                        process.Started,
                        AlreadyAvailable: !process.Started,
                        process,
                        version,
                        stopwatch.ElapsedMilliseconds);
                }

                await Task.Delay(PollInterval, startupCancellation.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (
            !cancellationToken.IsCancellationRequested && startupCancellation.IsCancellationRequested)
        {
            if (process.Started)
            {
                await serverManager.StopAsync(
                        context.Configuration.Paths.ConfigDirectory,
                        CancellationToken.None)
                    .ConfigureAwait(false);
            }

            throw new TimeoutException(
                $"Ollama did not become ready at {context.Configuration.Endpoint} within "
                + $"{context.Configuration.Timeout.TotalSeconds:0} seconds. "
                + $"Inspect '{process.StandardErrorLog}'.");
        }
        catch
        {
            if (process.Started)
            {
                await serverManager.StopAsync(
                        context.Configuration.Paths.ConfigDirectory,
                        CancellationToken.None)
                    .ConfigureAwait(false);
            }

            throw;
        }
    }

    private async Task<string?> TryGetVersionAsync(
        ResolvedOllamaConfiguration configuration,
        CancellationToken cancellationToken)
    {
        try
        {
            using var client = clientFactory.Create(configuration);
            return await client.GetVersionAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (IsUnavailable(exception, cancellationToken))
        {
            return null;
        }
    }

    private static bool IsUnavailable(Exception exception, CancellationToken cancellationToken) =>
        exception is OllamaClientException
            or HttpRequestException
            or IOException
            or InvalidOperationException
            or TimeoutException
        || exception is OperationCanceledException && !cancellationToken.IsCancellationRequested;

    private static bool IsLocalEndpoint(Uri endpoint) =>
        endpoint.IsLoopback
        || string.Equals(endpoint.Host, "localhost", StringComparison.OrdinalIgnoreCase)
        || IPAddress.TryParse(endpoint.Host, out var address)
        && (address.Equals(IPAddress.Any) || address.Equals(IPAddress.IPv6Any));
}
