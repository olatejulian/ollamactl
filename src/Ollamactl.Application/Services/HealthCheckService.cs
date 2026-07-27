using System.Diagnostics;
using System.Net;
using Ollamactl.Application.Abstractions;
using Ollamactl.Application.Diagnostics;
using Ollamactl.Application.Exceptions;
using Ollamactl.Application.Execution;
using Ollamactl.Application.Logging;
using Ollamactl.Application.Processes;
using Ollamactl.Application.Server;
using Ollamactl.Domain.Models;

namespace Ollamactl.Application.Services;

public sealed class HealthCheckService(
    IOllamaApiClientFactory clientFactory,
    IOllamaCommandRunner commandRunner,
    IOllamaServerManager serverManager,
    IOllamaProcessInspector processInspector,
    IOllamaLogReader logReader)
{
    public async Task<OllamaHealthReport> CheckAsync(
        HealthCheckRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.ExecutionContext);
        if (request.LogTail is < 1 or > 1000)
        {
            throw new ArgumentOutOfRangeException(
                nameof(request),
                request.LogTail,
                "The log tail must contain between 1 and 1000 lines.");
        }

        var startedAt = DateTimeOffset.UtcNow;
        var stopwatch = Stopwatch.StartNew();
        var context = request.ExecutionContext;
        var checks = new List<DiagnosticCheck>
        {
            new(
                "configuration",
                DiagnosticCheckStatus.Passed,
                "Configuration resolved successfully.",
                $"Host source: {context.Configuration.HostSource}; timeout source: {context.Configuration.TimeoutSource}.",
                null,
                0),
        };

        var localEndpoint = IsLocalEndpoint(context.Configuration.Endpoint);
        ManagedServerStatus? managedServer = null;
        IReadOnlyList<OllamaProcessInfo> processes = [];
        IReadOnlyList<OllamaLogTail> logs = [];

        if (localEndpoint)
        {
            var serverResult = await CheckManagedServerAsync(context.Configuration.Paths.ConfigDirectory, cancellationToken)
                .ConfigureAwait(false);
            managedServer = serverResult.Value;
            checks.Add(serverResult.Check);

            var processResult = await CheckProcessesAsync(cancellationToken).ConfigureAwait(false);
            processes = processResult.Value;
            checks.Add(processResult.Check);

            checks.Add(await CheckNativeCliAsync(request, cancellationToken).ConfigureAwait(false));

            if (request.IncludeLogs)
            {
                var logResult = await CheckLogsAsync(
                        context.Configuration.Paths.ConfigDirectory,
                        request.LogTail,
                        cancellationToken)
                    .ConfigureAwait(false);
                logs = logResult.Value;
                checks.Add(logResult.Check);
            }
            else
            {
                checks.Add(NotApplicable("logs", "Log inspection was not requested."));
            }
        }
        else
        {
            checks.Add(NotApplicable("managed-server", "Managed-process checks apply only to local endpoints."));
            checks.Add(NotApplicable("processes", "Local process inspection does not apply to a remote endpoint."));
            checks.Add(NotApplicable("native-cli", "Local CLI inspection does not apply to a remote endpoint."));
            checks.Add(NotApplicable("logs", "Local log inspection does not apply to a remote endpoint."));
        }

        using var client = clientFactory.Create(context.Configuration);
        var versionTask = CheckApiVersionAsync(client, cancellationToken);
        var modelsTask = CheckModelsAsync(client, cancellationToken);
        var runningModelsTask = CheckRunningModelsAsync(client, cancellationToken);
        await Task.WhenAll(versionTask, modelsTask, runningModelsTask).ConfigureAwait(false);

        var versionResult = await versionTask.ConfigureAwait(false);
        var modelResult = await modelsTask.ConfigureAwait(false);
        var runningModelResult = await runningModelsTask.ConfigureAwait(false);
        checks.Add(versionResult.Check);
        checks.Add(modelResult.Check);
        checks.Add(runningModelResult.Check);

        stopwatch.Stop();
        var status = versionResult.Check.Status == DiagnosticCheckStatus.Failed
            ? OllamaHealthStatus.Unhealthy
            : checks.Any(check => check.Status is DiagnosticCheckStatus.Warning or DiagnosticCheckStatus.Failed)
                ? OllamaHealthStatus.Degraded
                : OllamaHealthStatus.Healthy;

        return new OllamaHealthReport(
            startedAt,
            status,
            context.Configuration.Endpoint,
            versionResult.Value,
            modelResult.Value,
            runningModelResult.Value,
            managedServer,
            processes,
            logs,
            checks,
            stopwatch.ElapsedMilliseconds);
    }

    private async Task<DiagnosticCheck> CheckNativeCliAsync(
        HealthCheckRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.OllamaExecutable))
        {
            return new DiagnosticCheck(
                "native-cli",
                DiagnosticCheckStatus.Warning,
                "The native Ollama CLI could not be resolved.",
                request.ExecutableResolutionError,
                "Install Ollama, add it to PATH, set OLLAMA_EXE, or pass --ollama-path.",
                0);
        }

        var stopwatch = Stopwatch.StartNew();
        try
        {
            var result = await commandRunner.RunAsync(
                    new OllamaCommandRequest(
                        request.OllamaExecutable,
                        ["--version"],
                        request.ExecutionContext.EffectiveEnvironment,
                        Timeout: TimeSpan.FromSeconds(10)),
                    cancellationToken)
                .ConfigureAwait(false);
            stopwatch.Stop();

            var version = FirstNonEmpty(result.StandardOutput, result.StandardError);
            return result.ExitCode == 0
                ? new DiagnosticCheck(
                    "native-cli",
                    DiagnosticCheckStatus.Passed,
                    "The native Ollama CLI is executable.",
                    Truncate(version, 512),
                    null,
                    stopwatch.ElapsedMilliseconds)
                : new DiagnosticCheck(
                    "native-cli",
                    DiagnosticCheckStatus.Warning,
                    $"The native Ollama CLI returned exit code {result.ExitCode}.",
                    Truncate(version, 512),
                    "Run 'ollama --version' directly and verify the configured executable.",
                    stopwatch.ElapsedMilliseconds);
        }
        catch (Exception exception) when (IsExpectedFailure(exception, cancellationToken))
        {
            stopwatch.Stop();
            return Warning(
                "native-cli",
                "The native Ollama CLI could not be executed.",
                exception.Message,
                "Verify --ollama-path, OLLAMA_EXE, permissions, and PATH.",
                stopwatch.ElapsedMilliseconds);
        }
    }

    private async Task<CheckValue<ManagedServerStatus?>> CheckManagedServerAsync(
        string configDirectory,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            var status = await serverManager.GetStatusAsync(configDirectory, cancellationToken).ConfigureAwait(false);
            stopwatch.Stop();
            var checkStatus = status.State is ManagedServerState.Stale or ManagedServerState.Exited
                ? DiagnosticCheckStatus.Warning
                : DiagnosticCheckStatus.Passed;
            var action = status.State == ManagedServerState.Stale
                ? "Run 'ollamactl server stop' to clean stale state, then start the server again."
                : status.State == ManagedServerState.Exited
                    ? "Inspect 'ollamactl server logs' before restarting the server."
                    : null;
            return new CheckValue<ManagedServerStatus?>(
                status,
                new DiagnosticCheck(
                    "managed-server",
                    checkStatus,
                    $"Managed server state: {status.State}.",
                    status.ProcessId is null ? null : $"Ollama PID: {status.ProcessId}.",
                    action,
                    stopwatch.ElapsedMilliseconds));
        }
        catch (Exception exception) when (IsExpectedFailure(exception, cancellationToken))
        {
            stopwatch.Stop();
            return new CheckValue<ManagedServerStatus?>(
                null,
                Warning(
                    "managed-server",
                    "Managed server state could not be inspected.",
                    exception.Message,
                    "Verify access to the ollamactl configuration directory.",
                    stopwatch.ElapsedMilliseconds));
        }
    }

    private async Task<CheckValue<IReadOnlyList<OllamaProcessInfo>>> CheckProcessesAsync(
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            var value = await processInspector.InspectAsync(cancellationToken).ConfigureAwait(false);
            stopwatch.Stop();
            return new CheckValue<IReadOnlyList<OllamaProcessInfo>>(
                value,
                new DiagnosticCheck(
                    "processes",
                    DiagnosticCheckStatus.Passed,
                    $"Found {value.Count} local Ollama process(es).",
                    null,
                    null,
                    stopwatch.ElapsedMilliseconds));
        }
        catch (Exception exception) when (IsExpectedFailure(exception, cancellationToken))
        {
            stopwatch.Stop();
            return new CheckValue<IReadOnlyList<OllamaProcessInfo>>(
                [],
                Warning(
                    "processes",
                    "Local Ollama processes could not be inspected.",
                    exception.Message,
                    "Run the terminal with sufficient process-query permissions.",
                    stopwatch.ElapsedMilliseconds));
        }
    }

    private async Task<CheckValue<IReadOnlyList<OllamaLogTail>>> CheckLogsAsync(
        string configDirectory,
        int maximumLines,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            var value = await logReader.ReadTailAsync(configDirectory, maximumLines, cancellationToken)
                .ConfigureAwait(false);
            stopwatch.Stop();
            var suspiciousLines = value.Sum(tail => tail.Lines.Count(line => line.IsSuspicious));
            var status = suspiciousLines > 0 ? DiagnosticCheckStatus.Warning : DiagnosticCheckStatus.Passed;
            return new CheckValue<IReadOnlyList<OllamaLogTail>>(
                value,
                new DiagnosticCheck(
                    "logs",
                    status,
                    value.Count == 0
                        ? "No readable Ollama logs were found."
                        : $"Inspected {value.Count} log source(s); {suspiciousLines} suspicious line(s).",
                    null,
                    suspiciousLines > 0 ? "Review the returned redacted log excerpts." : null,
                    stopwatch.ElapsedMilliseconds));
        }
        catch (Exception exception) when (IsExpectedFailure(exception, cancellationToken))
        {
            stopwatch.Stop();
            return new CheckValue<IReadOnlyList<OllamaLogTail>>(
                [],
                Warning(
                    "logs",
                    "Ollama logs could not be inspected.",
                    exception.Message,
                    "Verify access to the managed and official Ollama log directories.",
                    stopwatch.ElapsedMilliseconds));
        }
    }

    private static async Task<CheckValue<string?>> CheckApiVersionAsync(
        IOllamaApiClient client,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            var value = await client.GetVersionAsync(cancellationToken).ConfigureAwait(false);
            stopwatch.Stop();
            return new CheckValue<string?>(
                value,
                new DiagnosticCheck(
                    "api.version",
                    DiagnosticCheckStatus.Passed,
                    $"Ollama API version {value} is reachable.",
                    null,
                    null,
                    stopwatch.ElapsedMilliseconds));
        }
        catch (Exception exception) when (IsExpectedFailure(exception, cancellationToken))
        {
            stopwatch.Stop();
            return new CheckValue<string?>(
                null,
                new DiagnosticCheck(
                    "api.version",
                    DiagnosticCheckStatus.Failed,
                    "The Ollama API is not reachable.",
                    exception.Message,
                    "Verify OLLAMA_HOST, start the server, and check firewall or proxy settings.",
                    stopwatch.ElapsedMilliseconds));
        }
    }

    private static async Task<CheckValue<IReadOnlyList<OllamaModel>>> CheckModelsAsync(
        IOllamaApiClient client,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            var value = await client.GetModelsAsync(cancellationToken).ConfigureAwait(false);
            stopwatch.Stop();
            return new CheckValue<IReadOnlyList<OllamaModel>>(
                value,
                new DiagnosticCheck(
                    "api.models",
                    DiagnosticCheckStatus.Passed,
                    $"The API reported {value.Length} installed model(s).",
                    null,
                    null,
                    stopwatch.ElapsedMilliseconds));
        }
        catch (Exception exception) when (IsExpectedFailure(exception, cancellationToken))
        {
            stopwatch.Stop();
            return new CheckValue<IReadOnlyList<OllamaModel>>(
                [],
                Warning(
                    "api.models",
                    "Installed models could not be queried.",
                    exception.Message,
                    "Inspect the server logs and retry 'ollamactl model list'.",
                    stopwatch.ElapsedMilliseconds));
        }
    }

    private static async Task<CheckValue<IReadOnlyList<RunningOllamaModel>>> CheckRunningModelsAsync(
        IOllamaApiClient client,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            var value = await client.GetRunningModelsAsync(cancellationToken).ConfigureAwait(false);
            stopwatch.Stop();
            return new CheckValue<IReadOnlyList<RunningOllamaModel>>(
                value,
                new DiagnosticCheck(
                    "api.running-models",
                    DiagnosticCheckStatus.Passed,
                    $"The API reported {value.Length} running model(s).",
                    null,
                    null,
                    stopwatch.ElapsedMilliseconds));
        }
        catch (Exception exception) when (IsExpectedFailure(exception, cancellationToken))
        {
            stopwatch.Stop();
            return new CheckValue<IReadOnlyList<RunningOllamaModel>>(
                [],
                Warning(
                    "api.running-models",
                    "Running models could not be queried.",
                    exception.Message,
                    "Inspect the server logs and retry 'ollamactl model running'.",
                    stopwatch.ElapsedMilliseconds));
        }
    }

    private static DiagnosticCheck Warning(
        string id,
        string summary,
        string? details,
        string? suggestedAction,
        long durationMilliseconds) => new(
        id,
        DiagnosticCheckStatus.Warning,
        summary,
        Truncate(details, 1024),
        suggestedAction,
        durationMilliseconds);

    private static DiagnosticCheck NotApplicable(string id, string summary) => new(
        id,
        DiagnosticCheckStatus.NotApplicable,
        summary,
        null,
        null,
        0);

    private static bool IsLocalEndpoint(Uri endpoint) =>
        endpoint.IsLoopback
        || string.Equals(endpoint.Host, "localhost", StringComparison.OrdinalIgnoreCase)
        || IPAddress.TryParse(endpoint.Host, out var address)
        && (address.Equals(IPAddress.Any) || address.Equals(IPAddress.IPv6Any));

    private static bool IsExpectedFailure(Exception exception, CancellationToken cancellationToken) =>
        exception is OllamaClientException
            or HttpRequestException
            or IOException
            or InvalidOperationException
            or TimeoutException
            or UnauthorizedAccessException
            or System.ComponentModel.Win32Exception
        || exception is OperationCanceledException && !cancellationToken.IsCancellationRequested;

    private static string? FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim();

    private static string? Truncate(string? value, int maximumLength) =>
        string.IsNullOrWhiteSpace(value) || value.Length <= maximumLength
            ? value
            : string.Concat(value.AsSpan(0, maximumLength), "…");

    private sealed record CheckValue<T>(T Value, DiagnosticCheck Check);
}
