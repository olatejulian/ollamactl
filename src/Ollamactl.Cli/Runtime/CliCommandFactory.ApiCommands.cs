using System.CommandLine;

namespace Ollamactl.Cli.Runtime;

public sealed partial class CliCommandFactory
{
    private Command CreateStatusCommand()
    {
        var command = new Command("status", "Check API health, version, and model state.");
        command.SetAction((parseResult, cancellationToken) =>
            WithApiClientAsync(
                parseResult,
                async (service, _, format, token) =>
                {
                    output.WriteStatus(await service.GetStatusAsync(token).ConfigureAwait(false), format);
                    return ExitCodes.Success;
                },
                cancellationToken));
        return command;
    }

    private Command CreateModelCommand()
    {
        var group = new Command("model", "Inspect and manage models.");
        group.Aliases.Add("models");

        var list = new Command("list", "List installed models.");
        list.SetAction((parseResult, cancellationToken) =>
            WithApiClientAsync(
                parseResult,
                async (service, _, format, token) =>
                {
                    output.WriteModels(await service.GetModelsAsync(token).ConfigureAwait(false), format);
                    return ExitCodes.Success;
                },
                cancellationToken));

        var running = new Command("running", "List models currently loaded in memory.");
        running.Aliases.Add("ps");
        running.SetAction((parseResult, cancellationToken) =>
            WithApiClientAsync(
                parseResult,
                async (service, _, format, token) =>
                {
                    output.WriteRunningModels(
                        await service.GetRunningModelsAsync(token).ConfigureAwait(false),
                        format);
                    return ExitCodes.Success;
                },
                cancellationToken));

        var loadName = new Argument<string>("name") { Description = "Model name, optionally including a tag." };
        var keepAlive = new Option<string>("--keep-alive")
        {
            Description = "How long Ollama should keep the model loaded (default: 30m).",
            DefaultValueFactory = _ => "30m",
        };
        var load = new Command("load", "Load a model into memory.")
        {
            Arguments = { loadName },
            Options = { keepAlive },
        };
        load.SetAction((parseResult, cancellationToken) =>
            WithApiClientAsync(
                parseResult,
                async (service, _, format, token) =>
                {
                    var model = parseResult.GetRequiredValue(loadName);
                    var duration = parseResult.GetRequiredValue(keepAlive);
                    await service.LoadModelAsync(model, duration, token).ConfigureAwait(false);
                    output.WriteModelAction(
                        model,
                        "loaded",
                        $"Model {model} loaded (keep-alive: {duration}).",
                        format);
                    return ExitCodes.Success;
                },
                cancellationToken));

        var unloadName = new Argument<string>("name") { Description = "Model name, optionally including a tag." };
        var unload = new Command("unload", "Unload a model from memory.")
        {
            Arguments = { unloadName },
        };
        unload.SetAction((parseResult, cancellationToken) =>
            WithApiClientAsync(
                parseResult,
                async (service, _, format, token) =>
                {
                    var model = parseResult.GetRequiredValue(unloadName);
                    await service.UnloadModelAsync(model, token).ConfigureAwait(false);
                    output.WriteModelAction(model, "unloaded", $"Model {model} unloaded.", format);
                    return ExitCodes.Success;
                },
                cancellationToken));

        group.Subcommands.Add(list);
        group.Subcommands.Add(running);
        group.Subcommands.Add(load);
        group.Subcommands.Add(unload);
        return group;
    }

    private Command CreateChatCommand()
    {
        var model = new Argument<string>("model") { Description = "Model name." };
        var prompt = new Argument<string>("prompt") { Description = "Prompt text." };
        var command = new Command("chat", "Send one non-streaming chat prompt through the REST API.")
        {
            Arguments = { model, prompt },
        };
        command.SetAction((parseResult, cancellationToken) =>
            WithApiClientAsync(
                parseResult,
                async (service, _, format, token) =>
                {
                    var result = await service.ChatAsync(
                            parseResult.GetRequiredValue(model),
                            parseResult.GetRequiredValue(prompt),
                            token)
                        .ConfigureAwait(false);
                    output.WriteChat(result, format);
                    return ExitCodes.Success;
                },
                cancellationToken));
        return command;
    }

    private Command CreateToolsCommand()
    {
        var group = new Command("tools", "Inspect model tool-calling support.");
        var model = new Argument<string>("model") { Description = "Model name." };
        var probe = new Command("probe", "Ask a model to call a deterministic test tool.")
        {
            Arguments = { model },
        };
        probe.SetAction((parseResult, cancellationToken) =>
            WithApiClientAsync(
                parseResult,
                async (service, _, format, token) =>
                {
                    var result = await service.ProbeToolsAsync(
                            parseResult.GetRequiredValue(model),
                            token)
                        .ConfigureAwait(false);
                    output.WriteToolProbe(result, format);
                    return result.CalledTool ? ExitCodes.Success : ExitCodes.GeneralError;
                },
                cancellationToken));
        group.Subcommands.Add(probe);
        return group;
    }
}
