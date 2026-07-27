using Ollamactl.Cli.Runtime;
using Ollamactl.Infrastructure.Configuration;
using Ollamactl.Infrastructure.Http;
using Ollamactl.Infrastructure.Logging;
using Ollamactl.Infrastructure.Processes;

using var cancellationSource = new CancellationTokenSource();
ConsoleCancelEventHandler cancelHandler = (_, eventArgs) =>
{
    eventArgs.Cancel = true;
    cancellationSource.Cancel();
};

Console.CancelKeyPress += cancelHandler;
try
{
    if (OllamaSupervisorEntryPoint.IsInvocation(args))
    {
        return await OllamaSupervisorEntryPoint.RunAsync(args, cancellationSource.Token).ConfigureAwait(false);
    }

    var pathResolver = new OllamactlPathResolver();
    var clientFactory = new OllamaHttpClientFactory();
    var applicationLaunch = ApplicationLaunchInfo.ForCurrentProcess(
        Environment.GetCommandLineArgs().FirstOrDefault());
    var commandFactory = new CliCommandFactory(
        new OllamactlConfigurationProvider(pathResolver),
        clientFactory,
        new OllamaExecutableResolver(),
        new OllamaCommandRunner(),
        new OllamaServerManager(pathResolver, applicationLaunch),
        new OllamaProcessInspector(),
        new OllamaLogReader(pathResolver),
        new SystemCliConsole());
    var application = new CliApplication(commandFactory.CreateRootCommand());
    return await application.RunAsync(args, cancellationSource.Token).ConfigureAwait(false);
}
finally
{
    Console.CancelKeyPress -= cancelHandler;
}
