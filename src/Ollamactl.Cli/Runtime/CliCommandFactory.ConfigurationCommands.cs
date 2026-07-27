using System.CommandLine;

namespace Ollamactl.Cli.Runtime;

public sealed partial class CliCommandFactory
{
    private Command CreateConfigCommand()
    {
        var group = new Command("config", "Inspect effective configuration without printing secret values.");

        var show = new Command("show", "Show the effective endpoint, timeout, paths, and executable.");
        show.SetAction((parseResult, cancellationToken) =>
            WriteConfigurationAsync(parseResult, cancellationToken));

        var environment = new Command("env", "List child environment variable names and origins, never values.");
        environment.SetAction((parseResult, cancellationToken) =>
            ExecuteAsync(
                parseResult,
                (context, format, _) =>
                {
                    var values = context.Origins
                        .OrderBy(item => item.Key, StringComparer.OrdinalIgnoreCase)
                        .Select(item => new EnvironmentOriginView(item.Key, item.Value))
                        .ToArray();
                    output.WriteEnvironmentOrigins(values, format);
                    return Task.FromResult(ExitCodes.Success);
                },
                cancellationToken));

        group.Subcommands.Add(show);
        group.Subcommands.Add(environment);
        return group;
    }

    private Command CreateEndpointCommand()
    {
        var command = new Command("endpoint", "Show the resolved Ollama endpoint and configuration source.");
        command.SetAction((parseResult, cancellationToken) =>
            WriteConfigurationAsync(parseResult, cancellationToken));
        return command;
    }

    private Task<int> WriteConfigurationAsync(
        ParseResult parseResult,
        CancellationToken cancellationToken) =>
        ExecuteAsync(
            parseResult,
            (context, format, _) =>
            {
                string? executable = null;
                try
                {
                    executable = ResolveExecutable(parseResult, context);
                }
                catch (Exception exception) when (exception is FileNotFoundException or InvalidOperationException)
                {
                    // The REST-only commands remain usable without a local native executable.
                }

                output.WriteConfiguration(
                    new ConfigurationView(
                        context.Configuration.Endpoint.ToString(),
                        (int)context.Configuration.Timeout.TotalSeconds,
                        context.Configuration.HostSource,
                        context.Configuration.TimeoutSource,
                        context.Configuration.Paths.ConfigDirectory,
                        context.EnvironmentFile,
                        context.Configuration.Paths.ServerStateFile,
                        executable),
                    format);
                return Task.FromResult(ExitCodes.Success);
            },
            cancellationToken);
}
