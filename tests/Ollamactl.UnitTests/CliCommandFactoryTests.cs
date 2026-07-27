using System.Text.Json;
using Ollamactl.Application.Exceptions;
using Ollamactl.Application.Execution;
using Ollamactl.Application.Logging;
using Ollamactl.Application.Processes;
using Ollamactl.Application.Server;
using Ollamactl.Cli.Runtime;
using Ollamactl.Infrastructure.Configuration;

namespace Ollamactl.UnitTests;

[Trait("Category", "Unit")]
public sealed class CliCommandFactoryTests
{
    [Fact]
    public async Task ConfigShowUsesCommandLineBeforeEnvironmentAndEnvFile()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        using var console = new TestCliConsole();
        File.WriteAllText(
            System.IO.Path.Combine(temporaryDirectory.Path, ".env"),
            "OLLAMA_HOST=file.example:3333\nOLLAMACTL_TIMEOUT_SECONDS=33\n");
        var client = new RecordingOllamaApiClient();
        var factory = new RecordingOllamaApiClientFactory(client);
        var application = CreateApplication(
            factory,
            new RecordingOllamaServerManager(),
            console,
            EnvironmentFrom(
                ("OLLAMA_HOST", "env.example:2222"),
                ("OLLAMACTL_TIMEOUT_SECONDS", "22")));

        var exitCode = await application.RunAsync(
            [
                "--config-dir", temporaryDirectory.Path,
                "--host", "cli.example:1111",
                "--timeout", "11",
                "--output", "json",
                "config", "show",
            ]).ConfigureAwait(true);

        Assert.Equal(ExitCodes.Success, exitCode);
        Assert.Equal(string.Empty, console.Error);
        Assert.Equal(0, factory.CreateCount);
        using var document = JsonDocument.Parse(console.Output);
        var root = document.RootElement;
        Assert.Equal("http://cli.example:1111/", root.GetProperty("host").GetString());
        Assert.Equal(11, root.GetProperty("timeoutSeconds").GetInt32());
        Assert.Equal("command-line", root.GetProperty("hostSource").GetString());
        Assert.Equal("command-line", root.GetProperty("timeoutSource").GetString());
        Assert.Equal(temporaryDirectory.Path, root.GetProperty("configDirectory").GetString());
    }

    [Fact]
    public async Task ConfigEnvListsNamesAndOriginsWithoutSecretValues()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        using var console = new TestCliConsole();
        File.WriteAllText(
            System.IO.Path.Combine(temporaryDirectory.Path, ".env"),
            "FILE_SECRET=file-secret-value\n");
        var application = CreateApplication(
            new RecordingOllamaApiClientFactory(new RecordingOllamaApiClient()),
            new RecordingOllamaServerManager(),
            console,
            EnvironmentFrom(("PROCESS_SECRET", "process-secret-value")));

        var exitCode = await application.RunAsync(
            [
                "--config-dir", temporaryDirectory.Path,
                "--output", "json",
                "config", "env",
            ]).ConfigureAwait(true);

        Assert.Equal(ExitCodes.Success, exitCode);
        Assert.Contains("FILE_SECRET", console.Output, StringComparison.Ordinal);
        Assert.Contains("PROCESS_SECRET", console.Output, StringComparison.Ordinal);
        Assert.DoesNotContain("file-secret-value", console.Output, StringComparison.Ordinal);
        Assert.DoesNotContain("process-secret-value", console.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ModelLoadForwardsArgumentsAndResolvedConfigurationToClient()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        using var console = new TestCliConsole();
        var client = new RecordingOllamaApiClient();
        var factory = new RecordingOllamaApiClientFactory(client);
        var application = CreateApplication(factory, new RecordingOllamaServerManager(), console);

        var exitCode = await application.RunAsync(
            [
                "--config-dir", temporaryDirectory.Path,
                "--host", "localhost:22434",
                "--timeout", "75",
                "model", "load", "qwen3:8b",
                "--keep-alive", "15m",
            ]).ConfigureAwait(true);

        Assert.Equal(ExitCodes.Success, exitCode);
        Assert.Equal(("qwen3:8b", "15m"), client.LoadedModel);
        Assert.True(client.IsDisposed);
        Assert.Equal(1, factory.CreateCount);
        Assert.NotNull(factory.Configuration);
        Assert.Equal(new Uri("http://localhost:22434/"), factory.Configuration.Endpoint);
        Assert.Equal(TimeSpan.FromSeconds(75), factory.Configuration.Timeout);
        Assert.Contains("Model qwen3:8b loaded", console.Output, StringComparison.Ordinal);
        Assert.Equal(string.Empty, console.Error);
    }

    [Fact]
    public async Task StatusClientFailureReturnsUnavailableAndJsonError()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        using var console = new TestCliConsole();
        var client = new RecordingOllamaApiClient
        {
            VersionException = new OllamaClientException("cannot reach Ollama"),
        };
        var factory = new RecordingOllamaApiClientFactory(client);
        var application = CreateApplication(factory, new RecordingOllamaServerManager(), console);

        var exitCode = await application.RunAsync(
            [
                "--config-dir", temporaryDirectory.Path,
                "--output", "json",
                "status",
            ]).ConfigureAwait(true);

        Assert.Equal(ExitCodes.Unavailable, exitCode);
        Assert.Equal(string.Empty, console.Output);
        Assert.True(client.IsDisposed);
        using var document = JsonDocument.Parse(console.Error);
        Assert.Equal(ExitCodes.Unavailable, document.RootElement.GetProperty("exitCode").GetInt32());
        Assert.Equal("cannot reach Ollama", document.RootElement.GetProperty("error").GetString());
    }

    [Fact]
    public async Task ConfigShowInvalidEnvFileTimeoutReturnsUsageErrorWithoutCreatingClient()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        using var console = new TestCliConsole();
        File.WriteAllText(
            System.IO.Path.Combine(temporaryDirectory.Path, ".env"),
            "OLLAMACTL_TIMEOUT_SECONDS=invalid\n");
        var factory = new RecordingOllamaApiClientFactory(new RecordingOllamaApiClient());
        var application = CreateApplication(factory, new RecordingOllamaServerManager(), console);

        var exitCode = await application.RunAsync(
            ["--config-dir", temporaryDirectory.Path, "config", "show"]).ConfigureAwait(true);

        Assert.Equal(ExitCodes.UsageError, exitCode);
        Assert.Equal(0, factory.CreateCount);
        Assert.Equal(string.Empty, console.Output);
        Assert.Contains("error: Invalid timeout value in env file.", console.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task NativeOllamaRoutesArgumentsEnvironmentAndExitCode()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        using var console = new TestCliConsole();
        var resolver = new RecordingOllamaExecutableResolver
        {
            Executable = "C:\\Resolved\\ollama.exe",
        };
        var runner = new RecordingOllamaCommandRunner
        {
            Result = new OllamaCommandResult(23, null, null),
        };
        var application = CreateApplication(
            new RecordingOllamaApiClientFactory(new RecordingOllamaApiClient()),
            new RecordingOllamaServerManager(),
            console,
            EnvironmentFrom(("CHILD_SETTING", "effective")),
            resolver,
            runner);

        var exitCode = await application.RunAsync(
            [
                "--config-dir", temporaryDirectory.Path,
                "--host", "child.example:11434",
                "--ollama-path", "C:\\Selected\\ollama.exe",
                "ollama", "list", "model with spaces",
            ]).ConfigureAwait(true);

        Assert.Equal(23, exitCode);
        Assert.Equal("C:\\Selected\\ollama.exe", resolver.CommandLineExecutable);
        Assert.NotNull(runner.Request);
        Assert.Equal("C:\\Resolved\\ollama.exe", runner.Request.Executable);
        Assert.Equal(["list", "model with spaces"], runner.Request.Arguments);
        Assert.Equal(OllamaCommandMode.Foreground, runner.Request.Mode);
        Assert.Equal("child.example:11434", runner.Request.Environment["OLLAMA_HOST"]);
        Assert.Equal("effective", runner.Request.Environment["CHILD_SETTING"]);
    }

    [Fact]
    public async Task ServerStartForwardsResolvedExecutableAndEffectiveEnvironment()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        using var console = new TestCliConsole();
        var environmentFile = System.IO.Path.Combine(temporaryDirectory.Path, "custom.env");
        File.WriteAllText(environmentFile, "OLLAMA_MODELS=C:\\Models\n");
        var client = new RecordingOllamaApiClient();
        client.VersionResponses.Enqueue(new OllamaClientException("not ready"));
        client.VersionResponses.Enqueue("0.9.0");
        var clientFactory = new RecordingOllamaApiClientFactory(client);
        var serverManager = new RecordingOllamaServerManager
        {
            StartResult = new ServerStartResult(
                true,
                1234,
                5678,
                "C:\\Ollama\\ollama.exe",
                "state.json",
                "stdout.log",
                "stderr.log"),
        };
        var resolver = new RecordingOllamaExecutableResolver
        {
            Executable = "C:\\Ollama\\ollama.exe",
        };
        var application = CreateApplication(
            clientFactory,
            serverManager,
            console,
            executableResolver: resolver);

        var exitCode = await application.RunAsync(
            [
                "--config-dir", temporaryDirectory.Path,
                "--ollama-path", "C:\\Selected\\ollama.exe",
                "--env-file", environmentFile,
                "server", "start",
            ]).ConfigureAwait(true);

        Assert.Equal(ExitCodes.Success, exitCode);
        Assert.NotNull(serverManager.StartOptions);
        Assert.Equal(temporaryDirectory.Path, serverManager.StartOptions.ConfigDirectory);
        Assert.Equal("C:\\Ollama\\ollama.exe", serverManager.StartOptions.OllamaExecutable);
        Assert.Equal("C:\\Models", serverManager.StartOptions.EnvironmentVariables["OLLAMA_MODELS"]);
        Assert.Equal("C:\\Selected\\ollama.exe", resolver.CommandLineExecutable);
        Assert.Contains("supervisor PID 1234, Ollama PID 5678", console.Output, StringComparison.Ordinal);
        Assert.Contains("API ready: version 0.9.0", console.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task HealthRoutesLocalDiagnosticsAndReturnsDegradedForSuspiciousLogs()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        using var console = new TestCliConsole();
        var runner = new RecordingOllamaCommandRunner();
        var processInspector = new RecordingOllamaProcessInspector
        {
            Processes =
            [
                new OllamaProcessInfo(
                    42,
                    "ollama",
                    "C:\\Ollama\\ollama.exe",
                    DateTimeOffset.UtcNow,
                    TimeSpan.Zero,
                    1024),
            ],
        };
        var logReader = new RecordingOllamaLogReader
        {
            Logs =
            [
                new OllamaLogTail(
                    OllamaLogSource.ManagedStandardError,
                    "stderr.log",
                    [new OllamaLogLine(1, "error token=[REDACTED]", true, true)]),
            ],
        };
        var application = CreateApplication(
            new RecordingOllamaApiClientFactory(new RecordingOllamaApiClient()),
            new RecordingOllamaServerManager(),
            console,
            commandRunner: runner,
            processInspector: processInspector,
            logReader: logReader);

        var exitCode = await application.RunAsync(
            [
                "--config-dir", temporaryDirectory.Path,
                "--output", "json",
                "health", "--include-logs", "--log-tail", "7",
            ]).ConfigureAwait(true);

        Assert.Equal(ExitCodes.GeneralError, exitCode);
        Assert.Equal(1, runner.RunCount);
        Assert.NotNull(runner.Request);
        Assert.Equal(["--version"], runner.Request.Arguments);
        Assert.Equal(OllamaCommandMode.Capture, runner.Request.Mode);
        Assert.Equal(TimeSpan.FromSeconds(10), runner.Request.Timeout);
        Assert.Equal(1, processInspector.InspectCount);
        Assert.Equal(temporaryDirectory.Path, logReader.ConfigDirectory);
        Assert.Equal(7, logReader.MaximumLines);
        using var document = JsonDocument.Parse(console.Output);
        Assert.Equal("Degraded", document.RootElement.GetProperty("status").GetString());
        var logCheck = document.RootElement
            .GetProperty("checks")
            .EnumerateArray()
            .Single(check => check.GetProperty("id").GetString() == "logs");
        Assert.Equal("Warning", logCheck.GetProperty("status").GetString());
    }

    private static CliApplication CreateApplication(
        RecordingOllamaApiClientFactory clientFactory,
        RecordingOllamaServerManager serverManager,
        TestCliConsole console,
        IReadOnlyDictionary<string, string>? processEnvironment = null,
        RecordingOllamaExecutableResolver? executableResolver = null,
        RecordingOllamaCommandRunner? commandRunner = null,
        RecordingOllamaProcessInspector? processInspector = null,
        RecordingOllamaLogReader? logReader = null)
    {
        var environment = processEnvironment ?? EnvironmentFrom();
        var configurationProvider = new OllamactlConfigurationProvider(
            new OllamactlPathResolver(),
            () => environment);
        var commandFactory = new CliCommandFactory(
            configurationProvider,
            clientFactory,
            executableResolver ?? new RecordingOllamaExecutableResolver(),
            commandRunner ?? new RecordingOllamaCommandRunner(),
            serverManager,
            processInspector ?? new RecordingOllamaProcessInspector(),
            logReader ?? new RecordingOllamaLogReader(),
            console);
        return new CliApplication(commandFactory.CreateRootCommand());
    }

    private static Dictionary<string, string> EnvironmentFrom(
        params (string Name, string Value)[] entries) =>
        entries.ToDictionary(
            entry => entry.Name,
            entry => entry.Value,
            StringComparer.OrdinalIgnoreCase);
}
