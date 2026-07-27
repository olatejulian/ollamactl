using System.Reflection;
using System.Text.Json;
using Ollamactl.Cli.Runtime;
using Ollamactl.TestKit;
using Xunit;

namespace Ollamactl.EndToEndTests;

[Trait("Category", "EndToEnd")]
public sealed class CliEndToEndTests
{
    [Fact]
    public async Task HelpPrintsUsageToStandardOutput()
    {
        var result = await CliProcessRunner.RunAsync("--help");

        Assert.Equal(0, result.ExitCode);
        Assert.Empty(result.StandardError);
        Assert.Contains("Usage:", result.StandardOutput, StringComparison.Ordinal);
        Assert.Contains("ollamactl [command] [options]", result.StandardOutput, StringComparison.Ordinal);
        foreach (var command in new[]
                 {
                     "ollama <arguments>",
                     "status",
                     "model, models",
                     "chat <model> <prompt>",
                     "tools",
                     "server",
                     "process, processes",
                     "doctor, health",
                     "config",
                     "endpoint",
                 })
        {
            Assert.Contains(command, result.StandardOutput, StringComparison.Ordinal);
        }
    }

    [Theory]
    [InlineData("ollama")]
    [InlineData("status")]
    [InlineData("model")]
    [InlineData("chat")]
    [InlineData("tools")]
    [InlineData("server")]
    [InlineData("process")]
    [InlineData("health")]
    [InlineData("config")]
    [InlineData("endpoint")]
    public async Task ModernCommandHelpIsAvailable(string command)
    {
        var result = await CliProcessRunner.RunAsync(command, "--help");

        Assert.Equal(0, result.ExitCode);
        Assert.Empty(result.StandardError);
        Assert.Contains("Usage:", result.StandardOutput, StringComparison.Ordinal);
        Assert.Contains($"ollamactl {command}", result.StandardOutput, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("models", "ollamactl model")]
    [InlineData("processes", "ollamactl process")]
    [InlineData("doctor", "ollamactl health")]
    public async Task ModernAliasesResolveToTheirCanonicalCommand(
        string alias,
        string canonicalUsage)
    {
        var result = await CliProcessRunner.RunAsync(alias, "--help");

        Assert.Equal(0, result.ExitCode);
        Assert.Empty(result.StandardError);
        Assert.Contains(canonicalUsage, result.StandardOutput, StringComparison.Ordinal);
    }

    [Fact]
    public async Task VersionPrintsTheCliInformationalVersion()
    {
        var result = await CliProcessRunner.RunAsync("--version");
        var expectedVersion = typeof(CliApplication).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion
            ?? throw new InvalidOperationException("The CLI has no informational version.");

        Assert.Equal(0, result.ExitCode);
        Assert.Empty(result.StandardError);
        Assert.Equal(expectedVersion, result.StandardOutput.Trim());
    }

    [Fact]
    public async Task InvalidCommandReturnsUsageErrorAndKeepsDiagnosticsOnStandardError()
    {
        var result = await CliProcessRunner.RunAsync("definitely-not-a-command");

        Assert.Equal(2, result.ExitCode);
        Assert.Contains("Usage:", result.StandardOutput, StringComparison.Ordinal);
        Assert.DoesNotContain("Unrecognized command", result.StandardOutput, StringComparison.Ordinal);
        Assert.Contains("Unrecognized command or argument 'definitely-not-a-command'", result.StandardError, StringComparison.Ordinal);
    }

    [Fact]
    public async Task StatusWithJsonOutputReportsTheFakeServerState()
    {
        await using var server = await FakeOllamaServer.StartAsync();

        var result = await RunJsonCommandAsync(server, "status");

        using var document = ParseSuccessfulJson(result);
        var root = document.RootElement;
        Assert.Equal(server.BaseUri, new Uri(root.GetProperty("endpoint").GetString()!, UriKind.Absolute));
        Assert.Equal(FakeOllamaServer.TestVersion, root.GetProperty("version").GetString());
        Assert.Equal(FakeOllamaServer.TestModel, root.GetProperty("models")[0].GetProperty("name").GetString());
        Assert.Equal(FakeOllamaServer.TestModel, root.GetProperty("runningModels")[0].GetProperty("name").GetString());
        Assert.Equal("GET", Assert.Single(server.GetRequests("/api/version")).Method);
        Assert.Equal("GET", Assert.Single(server.GetRequests("/api/tags")).Method);
        Assert.Equal("GET", Assert.Single(server.GetRequests("/api/ps")).Method);
    }

    [Fact]
    public async Task ModelListPrintsModelsReturnedByTheFakeServer()
    {
        await using var server = await FakeOllamaServer.StartAsync();

        var result = await RunJsonCommandAsync(server, "model", "list");

        using var document = ParseSuccessfulJson(result);
        var models = document.RootElement;
        Assert.Equal(JsonValueKind.Array, models.ValueKind);
        Assert.Equal(1, models.GetArrayLength());
        Assert.Equal(FakeOllamaServer.TestModel, models[0].GetProperty("name").GetString());
        Assert.Equal("GET", Assert.Single(server.GetRequests("/api/tags")).Method);
    }

    [Fact]
    public async Task ModelAndRunningAliasesPrintModelsReturnedByTheFakeServer()
    {
        await using var server = await FakeOllamaServer.StartAsync();

        var result = await RunJsonCommandAsync(server, "models", "ps");

        using var document = ParseSuccessfulJson(result);
        var models = document.RootElement;
        Assert.Equal(JsonValueKind.Array, models.ValueKind);
        Assert.Equal(1, models.GetArrayLength());
        Assert.Equal(FakeOllamaServer.TestModel, models[0].GetProperty("name").GetString());
        Assert.Equal("GET", Assert.Single(server.GetRequests("/api/ps")).Method);
    }

    [Fact]
    public async Task ChatPrintsTheFakeAssistantResponse()
    {
        await using var server = await FakeOllamaServer.StartAsync();

        var result = await RunJsonCommandAsync(server, "chat", "test-model", "Hello from the end-to-end test");

        using var document = ParseSuccessfulJson(result);
        var root = document.RootElement;
        Assert.Equal("test-model", root.GetProperty("model").GetString());
        Assert.Equal(FakeOllamaServer.TestChatContent, root.GetProperty("content").GetString());

        var request = Assert.Single(server.GetRequests("/api/chat"));
        Assert.Equal("POST", request.Method);
        using var requestDocument = JsonDocument.Parse(request.Body);
        Assert.Equal("test-model", requestDocument.RootElement.GetProperty("model").GetString());
        Assert.Equal(
            "Hello from the end-to-end test",
            requestDocument.RootElement.GetProperty("messages")[0].GetProperty("content").GetString());
    }

    [Fact]
    public async Task ToolsProbeReportsTheFakeToolCall()
    {
        await using var server = await FakeOllamaServer.StartAsync();

        var result = await RunJsonCommandAsync(server, "tools", "probe", "test-model");

        using var document = ParseSuccessfulJson(result);
        var root = document.RootElement;
        Assert.True(root.GetProperty("calledTool").GetBoolean());
        Assert.Equal("get_weather", root.GetProperty("toolName").GetString());
        using var argumentsDocument = JsonDocument.Parse(root.GetProperty("argumentsJson").GetString()!);
        Assert.Equal("Sao Paulo", argumentsDocument.RootElement.GetProperty("city").GetString());

        var request = Assert.Single(server.GetRequests("/api/chat"));
        Assert.Equal("POST", request.Method);
        using var requestDocument = JsonDocument.Parse(request.Body);
        Assert.Equal("get_weather", requestDocument.RootElement
            .GetProperty("tools")[0]
            .GetProperty("function")
            .GetProperty("name")
            .GetString());
    }

    [Fact]
    public async Task NativePassthroughPreservesArgumentsEnvironmentPrecedenceAndExitCode()
    {
        await using var sandbox = new TemporaryTestDirectory();
        var configDirectory = sandbox.CreateDirectory("config");
        var captureFile = sandbox.GetPath("native invocation.txt");
        var environmentFile = sandbox.GetPath("native.env");
        var fakeOllama = CreateFakeOllamaExecutable(sandbox);
        await File.WriteAllTextAsync(
            environmentFile,
            $"""
            OLLAMA_HOST=http://file-host.example:2345
            FAKE_CAPTURE_FILE={captureFile}
            FAKE_EXIT_CODE=37
            FILE_ONLY=from-env-file
            SHARED_SETTING=from-env-file
            """);
        var options = new CliProcessRunOptions(
            configDirectory,
            new Dictionary<string, string?>
            {
                ["SHARED_SETTING"] = "from-process",
                ["PROCESS_ONLY"] = "from-process",
            });

        var result = await CliProcessRunner.RunAsync(
            options,
            "--env-file", environmentFile,
            "--host", "http://cli-host.example:3456",
            "--ollama-path", fakeOllama,
            "ollama", "--",
            "list",
            "model with spaces",
            "--flag=value with spaces");

        Assert.Equal(37, result.ExitCode);
        Assert.Empty(result.StandardError);
        Assert.Contains("fake-ollama version 9.9.9", result.StandardOutput, StringComparison.Ordinal);
        Assert.True(File.Exists(captureFile));
        var captured = ParseCapturedValues(await File.ReadAllLinesAsync(captureFile));
        Assert.Equal("list", captured["argument0"]);
        Assert.Equal("model with spaces", captured["argument1"]);
        Assert.Equal("--flag=value with spaces", captured["argument2"]);
        Assert.Equal("http://cli-host.example:3456", captured["ollamaHost"]);
        Assert.Equal("from-env-file", captured["fileOnly"]);
        Assert.Equal("from-process", captured["sharedSetting"]);
        Assert.Equal("from-process", captured["processOnly"]);
    }

    [Fact]
    public async Task ConfigPrecedenceIsCommandLineThenProcessThenEnvironmentFile()
    {
        await using var sandbox = new TemporaryTestDirectory();
        var configDirectory = sandbox.CreateDirectory("config");
        var environmentFile = sandbox.GetPath("precedence.env");
        await File.WriteAllTextAsync(
            environmentFile,
            "OLLAMA_HOST=file.example:3333\nOLLAMACTL_TIMEOUT_SECONDS=33\n");

        var processOptions = new CliProcessRunOptions(
            configDirectory,
            new Dictionary<string, string?>
            {
                ["OLLAMA_HOST"] = "process.example:2222",
                ["OLLAMACTL_TIMEOUT_SECONDS"] = "22",
            });
        var commandLineResult = await CliProcessRunner.RunAsync(
            processOptions,
            "--env-file", environmentFile,
            "--host", "cli.example:1111",
            "--timeout", "11",
            "--output", "json",
            "config", "show");
        AssertConfiguration(
            commandLineResult,
            "http://cli.example:1111/",
            11,
            "command-line",
            "command-line");

        var processResult = await CliProcessRunner.RunAsync(
            processOptions,
            "--env-file", environmentFile,
            "--output", "json",
            "config", "show");
        AssertConfiguration(
            processResult,
            "http://process.example:2222/",
            22,
            "environment",
            "environment");

        var fileOptions = new CliProcessRunOptions(
            configDirectory,
            new Dictionary<string, string?>
            {
                ["OLLAMA_HOST"] = null,
                ["OLLAMACTL_TIMEOUT_SECONDS"] = null,
            });
        var fileResult = await CliProcessRunner.RunAsync(
            fileOptions,
            "--env-file", environmentFile,
            "--output", "json",
            "config", "show");
        AssertConfiguration(
            fileResult,
            "http://file.example:3333/",
            33,
            "env-file",
            "env-file");
    }

    [Fact]
    public async Task ConfigEnvironmentListsNamesAndOriginsWithoutExposingValues()
    {
        await using var sandbox = new TemporaryTestDirectory();
        var configDirectory = sandbox.CreateDirectory("config");
        var environmentFile = sandbox.GetPath("secrets.env");
        const string fileSecret = "e2e-file-secret-value-8472";
        const string processSecret = "e2e-process-secret-value-1936";
        await File.WriteAllTextAsync(environmentFile, $"E2E_FILE_SECRET={fileSecret}\n");
        var options = new CliProcessRunOptions(
            configDirectory,
            new Dictionary<string, string?>
            {
                ["E2E_PROCESS_SECRET"] = processSecret,
            });

        var result = await CliProcessRunner.RunAsync(
            options,
            "--env-file", environmentFile,
            "--output", "json",
            "config", "env");

        using var document = ParseSuccessfulJson(result);
        var variables = document.RootElement.EnumerateArray().ToArray();
        Assert.Contains(
            variables,
            variable => variable.GetProperty("name").GetString() == "E2E_FILE_SECRET"
                        && variable.GetProperty("source").GetString() == "env-file");
        Assert.Contains(
            variables,
            variable => variable.GetProperty("name").GetString() == "E2E_PROCESS_SECRET"
                        && variable.GetProperty("source").GetString() == "environment");
        Assert.All(variables, variable => Assert.False(variable.TryGetProperty("value", out _)));
        Assert.DoesNotContain(fileSecret, result.StandardOutput, StringComparison.Ordinal);
        Assert.DoesNotContain(processSecret, result.StandardOutput, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("health")]
    [InlineData("doctor")]
    public async Task HealthAndDoctorReportHealthyAgainstTheFakeServer(string command)
    {
        await using var server = await FakeOllamaServer.StartAsync();
        await using var sandbox = new TemporaryTestDirectory();
        var configDirectory = sandbox.CreateDirectory("config");
        var fakeOllama = CreateFakeOllamaExecutable(sandbox);

        var result = await CliProcessRunner.RunAsync(
            new CliProcessRunOptions(configDirectory),
            "--host", server.BaseUri.AbsoluteUri,
            "--timeout", "5",
            "--ollama-path", fakeOllama,
            "--output", "json",
            command);

        using var document = ParseSuccessfulJson(result);
        var root = document.RootElement;
        Assert.Equal("Healthy", root.GetProperty("status").GetString());
        Assert.Equal(FakeOllamaServer.TestVersion, root.GetProperty("apiVersion").GetString());
        Assert.Equal(FakeOllamaServer.TestModel, root.GetProperty("models")[0].GetProperty("name").GetString());
        Assert.Equal(
            FakeOllamaServer.TestModel,
            root.GetProperty("runningModels")[0].GetProperty("name").GetString());
        Assert.DoesNotContain(
            root.GetProperty("checks").EnumerateArray(),
            check => check.GetProperty("status").GetString() is "Warning" or "Failed");
        Assert.Equal("GET", Assert.Single(server.GetRequests("/api/version")).Method);
        Assert.Equal("GET", Assert.Single(server.GetRequests("/api/tags")).Method);
        Assert.Equal("GET", Assert.Single(server.GetRequests("/api/ps")).Method);
    }

    [Fact]
    public async Task ProcessesAliasListsNoManagedProcessInAFreshSandbox()
    {
        await using var sandbox = new TemporaryTestDirectory();
        var configDirectory = sandbox.CreateDirectory("config");

        var result = await CliProcessRunner.RunAsync(
            new CliProcessRunOptions(configDirectory),
            "--output", "json",
            "processes", "list");

        using var document = ParseSuccessfulJson(result);
        Assert.Equal(JsonValueKind.Array, document.RootElement.ValueKind);
        Assert.Equal(0, document.RootElement.GetArrayLength());
    }

    private static Task<CliProcessResult> RunJsonCommandAsync(
        FakeOllamaServer server,
        params string[] commandArguments)
    {
        var arguments = new[]
        {
            "--host",
            server.BaseUri.AbsoluteUri,
            "--timeout",
            "5",
            "--output",
            "json",
        }.Concat(commandArguments).ToArray();

        return CliProcessRunner.RunAsync(arguments);
    }

    private static JsonDocument ParseSuccessfulJson(CliProcessResult result)
    {
        Assert.Equal(0, result.ExitCode);
        Assert.Empty(result.StandardError);
        Assert.False(string.IsNullOrWhiteSpace(result.StandardOutput));
        return JsonDocument.Parse(result.StandardOutput);
    }

    private static void AssertConfiguration(
        CliProcessResult result,
        string expectedHost,
        int expectedTimeout,
        string expectedHostSource,
        string expectedTimeoutSource)
    {
        using var document = ParseSuccessfulJson(result);
        var root = document.RootElement;
        Assert.Equal(expectedHost, root.GetProperty("host").GetString());
        Assert.Equal(expectedTimeout, root.GetProperty("timeoutSeconds").GetInt32());
        Assert.Equal(expectedHostSource, root.GetProperty("hostSource").GetString());
        Assert.Equal(expectedTimeoutSource, root.GetProperty("timeoutSource").GetString());
    }

    private static Dictionary<string, string> ParseCapturedValues(IEnumerable<string> lines) =>
        lines.Select(line => line.Split('=', 2))
            .ToDictionary(parts => parts[0], parts => parts[1], StringComparer.Ordinal);

    private static string CreateFakeOllamaExecutable(TemporaryTestDirectory sandbox)
    {
        var executableDirectory = sandbox.CreateDirectory("fake native with spaces");
        if (OperatingSystem.IsWindows())
        {
            var executable = Path.Combine(executableDirectory, "fake ollama.cmd");
            File.WriteAllText(
                executable,
                """
                @echo off
                setlocal DisableDelayedExpansion
                if defined FAKE_CAPTURE_FILE (
                  >"%FAKE_CAPTURE_FILE%" echo argument0=%~1
                  >>"%FAKE_CAPTURE_FILE%" echo argument1=%~2
                  >>"%FAKE_CAPTURE_FILE%" echo argument2=%~3
                  >>"%FAKE_CAPTURE_FILE%" echo ollamaHost=%OLLAMA_HOST%
                  >>"%FAKE_CAPTURE_FILE%" echo fileOnly=%FILE_ONLY%
                  >>"%FAKE_CAPTURE_FILE%" echo sharedSetting=%SHARED_SETTING%
                  >>"%FAKE_CAPTURE_FILE%" echo processOnly=%PROCESS_ONLY%
                )
                echo fake-ollama version 9.9.9
                if not defined FAKE_EXIT_CODE exit /b 0
                exit /b %FAKE_EXIT_CODE%
                """.Replace("\n", "\r\n", StringComparison.Ordinal));
            return executable;
        }

        var unixExecutable = Path.Combine(executableDirectory, "fake-ollama");
        File.WriteAllText(
            unixExecutable,
            """
            #!/bin/sh
            if [ -n "${FAKE_CAPTURE_FILE:-}" ]; then
              {
                printf 'argument0=%s\n' "$1"
                printf 'argument1=%s\n' "$2"
                printf 'argument2=%s\n' "$3"
                printf 'ollamaHost=%s\n' "${OLLAMA_HOST:-}"
                printf 'fileOnly=%s\n' "${FILE_ONLY:-}"
                printf 'sharedSetting=%s\n' "${SHARED_SETTING:-}"
                printf 'processOnly=%s\n' "${PROCESS_ONLY:-}"
              } > "$FAKE_CAPTURE_FILE"
            fi
            printf 'fake-ollama version 9.9.9\n'
            exit "${FAKE_EXIT_CODE:-0}"
            """);
        File.SetUnixFileMode(
            unixExecutable,
            UnixFileMode.UserRead
            | UnixFileMode.UserWrite
            | UnixFileMode.UserExecute
            | UnixFileMode.GroupRead
            | UnixFileMode.GroupExecute
            | UnixFileMode.OtherRead
            | UnixFileMode.OtherExecute);
        return unixExecutable;
    }
}
