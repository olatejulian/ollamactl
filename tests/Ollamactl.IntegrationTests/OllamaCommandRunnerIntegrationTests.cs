using System.Collections;
using System.Text;
using Ollamactl.Application.Execution;
using Ollamactl.Infrastructure.Processes;
using Xunit;

namespace Ollamactl.IntegrationTests;

[Trait("Category", "Integration")]
public sealed class OllamaCommandRunnerIntegrationTests
{
    [Fact]
    public async Task CapturePassesArgumentsAndEffectiveEnvironment()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        await using var sandbox = await CommandRunnerSandbox.CreateAsync();
        var environment = sandbox.CreateEnvironment();
        environment["OLLAMACTL_TEST_VALUE"] = "effective-environment";
        var runner = new OllamaCommandRunner();
        var request = new OllamaCommandRequest(
            sandbox.FakeExecutable,
            ["capture", "two words", "--flag=value"],
            environment);

        var result = await runner.RunAsync(request, CancellationToken.None);

        Assert.Equal(0, result.ExitCode);
        Assert.NotNull(result.StandardOutput);
        Assert.NotNull(result.StandardError);
        Assert.Contains("captured stdout", result.StandardOutput, StringComparison.Ordinal);
        Assert.Contains("captured stderr", result.StandardError, StringComparison.Ordinal);
        var invocation = await File.ReadAllLinesAsync(sandbox.InvocationFile);
        Assert.Contains("first=[capture]", invocation);
        Assert.Contains("second=[two words]", invocation);
        Assert.Contains("third=[--flag=value]", invocation);
        Assert.Contains("environment=[effective-environment]", invocation);
    }

    [Fact]
    public async Task CapturePreservesNonZeroOllamaExitCode()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        await using var sandbox = await CommandRunnerSandbox.CreateAsync();
        var runner = new OllamaCommandRunner();
        var request = new OllamaCommandRequest(
            sandbox.FakeExecutable,
            ["exit", "23"],
            sandbox.CreateEnvironment());

        var result = await runner.RunAsync(request, CancellationToken.None);

        Assert.Equal(23, result.ExitCode);
    }

    [Fact]
    public async Task ForegroundInheritsStreamsAndDoesNotReturnCapturedText()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        await using var sandbox = await CommandRunnerSandbox.CreateAsync();
        var runner = new OllamaCommandRunner();
        var request = new OllamaCommandRequest(
            sandbox.FakeExecutable,
            ["exit", "7"],
            sandbox.CreateEnvironment(),
            OllamaCommandMode.Foreground);

        var result = await runner.RunAsync(request, CancellationToken.None);

        Assert.Equal(7, result.ExitCode);
        Assert.Null(result.StandardOutput);
        Assert.Null(result.StandardError);
    }

    [Fact]
    public async Task CancellationKillsTheOwnedProcessTree()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        await using var sandbox = await CommandRunnerSandbox.CreateAsync();
        using var cancellation = new CancellationTokenSource();
        var runner = new OllamaCommandRunner();
        var request = new OllamaCommandRequest(
            sandbox.FakeExecutable,
            ["wait"],
            sandbox.CreateEnvironment());

        var execution = runner.RunAsync(request, cancellation.Token);
        await WaitForFileAsync(sandbox.StartedFile);
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => execution);
        await Task.Delay(TimeSpan.FromMilliseconds(300));
        Assert.False(File.Exists(sandbox.CompletedFile));
    }

    [Fact]
    public async Task TimeoutKillsTheOwnedProcessTree()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        await using var sandbox = await CommandRunnerSandbox.CreateAsync();
        var runner = new OllamaCommandRunner();
        var request = new OllamaCommandRequest(
            sandbox.FakeExecutable,
            ["wait"],
            sandbox.CreateEnvironment(),
            Timeout: TimeSpan.FromMilliseconds(300));

        await Assert.ThrowsAsync<TimeoutException>(() =>
            runner.RunAsync(request, CancellationToken.None));
        Assert.False(File.Exists(sandbox.CompletedFile));
    }

    private static async Task WaitForFileAsync(string path)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (!File.Exists(path))
        {
            await Task.Delay(TimeSpan.FromMilliseconds(25), timeout.Token);
        }
    }

    private sealed class CommandRunnerSandbox : IAsyncDisposable
    {
        private CommandRunnerSandbox(string rootDirectory)
        {
            RootDirectory = rootDirectory;
            FakeExecutable = Path.Combine(rootDirectory, "fake-ollama.cmd");
            InvocationFile = Path.Combine(rootDirectory, "invocation.txt");
            StartedFile = Path.Combine(rootDirectory, "started.txt");
            CompletedFile = Path.Combine(rootDirectory, "completed.txt");
        }

        public string RootDirectory { get; }

        public string FakeExecutable { get; }

        public string InvocationFile { get; }

        public string StartedFile { get; }

        public string CompletedFile { get; }

        public static async Task<CommandRunnerSandbox> CreateAsync()
        {
            var rootDirectory = Path.Combine(
                Path.GetTempPath(),
                "ollamactl-command-runner-tests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(rootDirectory);
            var sandbox = new CommandRunnerSandbox(rootDirectory);
            await File.WriteAllTextAsync(
                sandbox.FakeExecutable,
                CreateFakeExecutableScript(),
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                CancellationToken.None);
            return sandbox;
        }

        public Dictionary<string, string> CreateEnvironment()
        {
            var environment = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (DictionaryEntry variable in Environment.GetEnvironmentVariables())
            {
                if (variable.Key is string name && variable.Value is string value)
                {
                    environment[name] = value;
                }
            }

            environment["OLLAMACTL_TEST_RECORD"] = InvocationFile;
            environment["OLLAMACTL_TEST_STARTED"] = StartedFile;
            environment["OLLAMACTL_TEST_COMPLETED"] = CompletedFile;
            return environment;
        }

        public async ValueTask DisposeAsync()
        {
            for (var attempt = 0; attempt < 10; attempt++)
            {
                try
                {
                    if (Directory.Exists(RootDirectory))
                    {
                        Directory.Delete(RootDirectory, recursive: true);
                    }

                    break;
                }
                catch (IOException) when (attempt < 9)
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(100));
                }
                catch (UnauthorizedAccessException) when (attempt < 9)
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(100));
                }
            }

            GC.SuppressFinalize(this);
        }

        private static string CreateFakeExecutableScript() =>
            string.Join(
                Environment.NewLine,
                "@echo off",
                "setlocal",
                "> \"%OLLAMACTL_TEST_RECORD%\" echo first=[%~1]",
                ">> \"%OLLAMACTL_TEST_RECORD%\" echo second=[%~2]",
                ">> \"%OLLAMACTL_TEST_RECORD%\" echo third=[%~3]",
                ">> \"%OLLAMACTL_TEST_RECORD%\" echo environment=[%OLLAMACTL_TEST_VALUE%]",
                "if /i \"%~1\"==\"wait\" goto wait",
                "if /i \"%~1\"==\"exit\" exit /b %~2",
                "echo captured stdout",
                "1>&2 echo captured stderr",
                "exit /b 0",
                ":wait",
                "> \"%OLLAMACTL_TEST_STARTED%\" echo started",
                "\"%SystemRoot%\\System32\\ping.exe\" -n 31 127.0.0.1 >nul",
                "> \"%OLLAMACTL_TEST_COMPLETED%\" echo completed",
                "exit /b 0",
                string.Empty);
    }
}
