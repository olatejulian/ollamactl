using System.CommandLine;
using Ollamactl.Application.Execution;

namespace Ollamactl.Cli.Runtime;

public sealed partial class CliCommandFactory
{
    private Command CreateNativeOllamaCommand()
    {
        var arguments = new Argument<string[]>("arguments")
        {
            Description = "Arguments forwarded verbatim after '--'.",
            Arity = ArgumentArity.ZeroOrMore,
        };
        var command = new Command("ollama", "Run the native Ollama CLI with the resolved environment.")
        {
            Arguments = { arguments },
        };
        command.SetAction((parseResult, cancellationToken) =>
            ExecuteAsync(
                parseResult,
                async (context, _, token) =>
                {
                    var result = await commandRunner.RunAsync(
                            new OllamaCommandRequest(
                                ResolveExecutable(parseResult, context),
                                parseResult.GetValue(arguments) ?? [],
                                context.EffectiveEnvironment,
                                OllamaCommandMode.Foreground),
                            token)
                        .ConfigureAwait(false);
                    return result.ExitCode;
                },
                cancellationToken));
        return command;
    }
}
