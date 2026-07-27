using System.CommandLine;

namespace Ollamactl.Cli.Runtime;

public sealed class CliApplication(RootCommand rootCommand)
{
    public async Task<int> RunAsync(string[] args, CancellationToken cancellationToken = default)
    {
        var parseResult = rootCommand.Parse(args);
        var exitCode = await parseResult.InvokeAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
        return parseResult.Errors.Count > 0 ? ExitCodes.UsageError : exitCode;
    }
}
