using System.CommandLine;
using Ollamactl.Application.Abstractions;
using Ollamactl.Application.Configuration;
using Ollamactl.Application.Exceptions;
using Ollamactl.Application.Services;

namespace Ollamactl.Cli.Runtime;

public sealed partial class CliCommandFactory
{
    private readonly IOllamactlConfigurationProvider configurationProvider;
    private readonly IOllamaApiClientFactory clientFactory;
    private readonly IOllamaExecutableResolver executableResolver;
    private readonly IOllamaCommandRunner commandRunner;
    private readonly IOllamaServerManager serverManager;
    private readonly IOllamaProcessInspector processInspector;
    private readonly IOllamaLogReader logReader;
    private readonly CliOutput output;

    private readonly Option<string?> hostOption = new("--host")
    {
        Description = "Ollama host as host:port or an HTTP(S) origin without path, query, or fragment.",
        Recursive = true,
    };

    private readonly Option<int?> timeoutOption = new("--timeout")
    {
        Description = "HTTP and startup timeout in seconds (1-600).",
        Recursive = true,
    };

    private readonly Option<string?> configDirectoryOption = new("--config-dir")
    {
        Description = "ollamactl configuration directory.",
        Recursive = true,
    };

    private readonly Option<string?> environmentFileOption = new("--env-file")
    {
        Description = "Dotenv file used for configuration and native Ollama child processes.",
        Recursive = true,
    };

    private readonly Option<string?> ollamaPathOption = new("--ollama-path")
    {
        Description = "Native Ollama executable (otherwise OLLAMA_EXE or PATH).",
        Recursive = true,
    };

    private readonly Option<OutputFormat> outputOption = new("--output")
    {
        Description = "Output format: text or json.",
        DefaultValueFactory = _ => OutputFormat.Text,
        Recursive = true,
    };

    public CliCommandFactory(
        IOllamactlConfigurationProvider configurationProvider,
        IOllamaApiClientFactory clientFactory,
        IOllamaExecutableResolver executableResolver,
        IOllamaCommandRunner commandRunner,
        IOllamaServerManager serverManager,
        IOllamaProcessInspector processInspector,
        IOllamaLogReader logReader,
        ICliConsole console)
    {
        this.configurationProvider = configurationProvider
            ?? throw new ArgumentNullException(nameof(configurationProvider));
        this.clientFactory = clientFactory ?? throw new ArgumentNullException(nameof(clientFactory));
        this.executableResolver = executableResolver
            ?? throw new ArgumentNullException(nameof(executableResolver));
        this.commandRunner = commandRunner ?? throw new ArgumentNullException(nameof(commandRunner));
        this.serverManager = serverManager ?? throw new ArgumentNullException(nameof(serverManager));
        this.processInspector = processInspector ?? throw new ArgumentNullException(nameof(processInspector));
        this.logReader = logReader ?? throw new ArgumentNullException(nameof(logReader));
        output = new CliOutput(console ?? throw new ArgumentNullException(nameof(console)));
    }

    public RootCommand CreateRootCommand() => new(
        "A modern hybrid wrapper for the Ollama CLI and REST API.")
    {
        Options =
        {
            hostOption,
            timeoutOption,
            configDirectoryOption,
            environmentFileOption,
            ollamaPathOption,
            outputOption,
        },
        Subcommands =
        {
            CreateNativeOllamaCommand(),
            CreateStatusCommand(),
            CreateModelCommand(),
            CreateChatCommand(),
            CreateToolsCommand(),
            CreateServerCommand(),
            CreateProcessCommand(),
            CreateHealthCommand(),
            CreateConfigCommand(),
            CreateEndpointCommand(),
        },
    };

    private Task<int> WithApiClientAsync(
        ParseResult parseResult,
        Func<OllamaService, ResolvedExecutionContext, OutputFormat, CancellationToken, Task<int>> action,
        CancellationToken cancellationToken) =>
        ExecuteAsync(
            parseResult,
            async (context, format, token) =>
            {
                using var client = clientFactory.Create(context.Configuration);
                return await action(new OllamaService(client), context, format, token).ConfigureAwait(false);
            },
            cancellationToken);

    private async Task<int> ExecuteAsync(
        ParseResult parseResult,
        Func<ResolvedExecutionContext, OutputFormat, CancellationToken, Task<int>> action,
        CancellationToken cancellationToken)
    {
        var format = parseResult.GetValue(outputOption);
        try
        {
            var context = await configurationProvider.ResolveAsync(
                    new ConfigurationOverrides(
                        parseResult.GetValue(hostOption),
                        parseResult.GetValue(timeoutOption),
                        parseResult.GetValue(configDirectoryOption),
                        parseResult.GetValue(environmentFileOption)),
                    cancellationToken)
                .ConfigureAwait(false);
            return await action(context, format, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            output.WriteError(ExitCodes.Cancelled, "Operation cancelled.", format);
            return ExitCodes.Cancelled;
        }
        catch (OllamaClientException exception)
        {
            output.WriteError(ExitCodes.Unavailable, exception.Message, format);
            return ExitCodes.Unavailable;
        }
        catch (Exception exception) when (exception is FormatException or ArgumentOutOfRangeException)
        {
            output.WriteError(ExitCodes.UsageError, exception.Message, format);
            return ExitCodes.UsageError;
        }
        catch (Exception exception) when (exception is
            FileNotFoundException or
            IOException or
            UnauthorizedAccessException or
            InvalidOperationException or
            TimeoutException or
            System.ComponentModel.Win32Exception)
        {
            output.WriteError(ExitCodes.Unavailable, exception.Message, format);
            return ExitCodes.Unavailable;
        }
    }

    private string ResolveExecutable(ParseResult parseResult, ResolvedExecutionContext context) =>
        executableResolver.Resolve(
            parseResult.GetValue(ollamaPathOption),
            context.EffectiveEnvironment);
}
