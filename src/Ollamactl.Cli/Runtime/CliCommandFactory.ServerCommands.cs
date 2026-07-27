using System.CommandLine;
using Ollamactl.Application.Diagnostics;
using Ollamactl.Application.Services;

namespace Ollamactl.Cli.Runtime;

public sealed partial class CliCommandFactory
{
    private Command CreateServerCommand()
    {
        var group = new Command("server", "Manage the local Ollama server owned by ollamactl.");

        var start = new Command("start", "Start Ollama in the background and wait for API readiness.");
        start.SetAction((parseResult, cancellationToken) =>
            ExecuteAsync(
                parseResult,
                async (context, format, token) =>
                {
                    var lifecycle = new ServerLifecycleService(clientFactory, serverManager);
                    var result = await lifecycle.StartAsync(
                            context,
                            ResolveExecutable(parseResult, context),
                            token)
                        .ConfigureAwait(false);
                    output.WriteServerLaunch(result, format);
                    return ExitCodes.Success;
                },
                cancellationToken));

        var stop = new Command("stop", "Stop only a server whose process identity is owned by ollamactl.");
        stop.SetAction((parseResult, cancellationToken) =>
            ExecuteAsync(
                parseResult,
                async (context, format, token) =>
                {
                    var result = await serverManager.StopAsync(
                            context.Configuration.Paths.ConfigDirectory,
                            token)
                        .ConfigureAwait(false);
                    output.WriteServerStop(result, format);
                    return ExitCodes.Success;
                },
                cancellationToken));

        var restart = new Command("restart", "Stop the owned server, then start Ollama and wait for readiness.");
        restart.SetAction((parseResult, cancellationToken) =>
            ExecuteAsync(
                parseResult,
                async (context, format, token) =>
                {
                    var stopped = await serverManager.StopAsync(
                            context.Configuration.Paths.ConfigDirectory,
                            token)
                        .ConfigureAwait(false);
                    var lifecycle = new ServerLifecycleService(clientFactory, serverManager);
                    var started = await lifecycle.StartAsync(
                            context,
                            ResolveExecutable(parseResult, context),
                            token)
                        .ConfigureAwait(false);
                    output.WriteServerRestart(new ServerRestartView(stopped, started), format);
                    return ExitCodes.Success;
                },
                cancellationToken));

        var status = new Command("status", "Show persisted ownership and managed-process state.");
        status.SetAction((parseResult, cancellationToken) =>
            ExecuteAsync(
                parseResult,
                async (context, format, token) =>
                {
                    var value = await serverManager.GetStatusAsync(
                            context.Configuration.Paths.ConfigDirectory,
                            token)
                        .ConfigureAwait(false);
                    output.WriteManagedServerStatus(value, format);
                    return ExitCodes.Success;
                },
                cancellationToken));

        var logs = new Command("logs", "Read the last 100 redacted lines from managed and official logs.");
        logs.SetAction((parseResult, cancellationToken) =>
            ExecuteAsync(
                parseResult,
                async (context, format, token) =>
                {
                    var value = await logReader.ReadTailAsync(
                            context.Configuration.Paths.ConfigDirectory,
                            100,
                            token)
                        .ConfigureAwait(false);
                    output.WriteLogs(value, format);
                    return ExitCodes.Success;
                },
                cancellationToken));

        group.Subcommands.Add(start);
        group.Subcommands.Add(stop);
        group.Subcommands.Add(restart);
        group.Subcommands.Add(status);
        group.Subcommands.Add(logs);
        return group;
    }

    private Command CreateProcessCommand()
    {
        var group = new Command("process", "Inspect Ollama processes.");
        group.Aliases.Add("processes");
        var all = new Option<bool>("--all")
        {
            Description = "Include every local process whose name starts with 'ollama'.",
        };
        var list = new Command("list", "List the managed process, or every Ollama process with --all.")
        {
            Options = { all },
        };
        list.SetAction((parseResult, cancellationToken) =>
            ExecuteAsync(
                parseResult,
                async (context, format, token) =>
                {
                    var processes = await processInspector.InspectAsync(token).ConfigureAwait(false);
                    if (!parseResult.GetValue(all))
                    {
                        var managed = await serverManager.GetStatusAsync(
                                context.Configuration.Paths.ConfigDirectory,
                                token)
                            .ConfigureAwait(false);
                        processes = managed.ProcessId is { } processId
                            ? processes.Where(process => process.ProcessId == processId).ToArray()
                            : [];
                    }

                    output.WriteProcesses(processes, format);
                    return ExitCodes.Success;
                },
                cancellationToken));
        group.Subcommands.Add(list);
        return group;
    }

    private Command CreateHealthCommand()
    {
        var includeLogs = new Option<bool>("--include-logs")
        {
            Description = "Include bounded, redacted log excerpts.",
        };
        var logTail = new Option<int>("--log-tail")
        {
            Description = "Maximum lines from each log source (1-1000).",
            DefaultValueFactory = _ => 100,
        };
        var command = new Command("health", "Run complete API, CLI, process, model, and optional log diagnostics.")
        {
            Options = { includeLogs, logTail },
        };
        command.Aliases.Add("doctor");
        command.SetAction((parseResult, cancellationToken) =>
            ExecuteAsync(
                parseResult,
                async (context, format, token) =>
                {
                    string? executable = null;
                    string? resolutionError = null;
                    try
                    {
                        executable = ResolveExecutable(parseResult, context);
                    }
                    catch (Exception exception) when (exception is FileNotFoundException or InvalidOperationException)
                    {
                        resolutionError = exception.Message;
                    }

                    var service = new HealthCheckService(
                        clientFactory,
                        commandRunner,
                        serverManager,
                        processInspector,
                        logReader);
                    var report = await service.CheckAsync(
                            new HealthCheckRequest(
                                context,
                                executable,
                                resolutionError,
                                parseResult.GetValue(includeLogs),
                                parseResult.GetRequiredValue(logTail)),
                            token)
                        .ConfigureAwait(false);
                    output.WriteHealth(report, format);
                    return report.Status switch
                    {
                        OllamaHealthStatus.Healthy => ExitCodes.Success,
                        OllamaHealthStatus.Degraded => ExitCodes.GeneralError,
                        OllamaHealthStatus.Unhealthy => ExitCodes.Unavailable,
                        _ => ExitCodes.GeneralError,
                    };
                },
                cancellationToken));
        return command;
    }
}
