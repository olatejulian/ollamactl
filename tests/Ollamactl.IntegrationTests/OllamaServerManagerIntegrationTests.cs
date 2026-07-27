using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Ollamactl.Application.Server;
using Ollamactl.Infrastructure.Configuration;
using Ollamactl.Infrastructure.Processes;
using Xunit;

namespace Ollamactl.IntegrationTests;

[Trait("Category", "Integration")]
public sealed class OllamaServerManagerIntegrationTests
{
    private const string TestEnvironmentValue = "supervisor-environment-value";
    private static readonly TimeSpan ReadinessTimeout = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task StartCreatesCurrentStateLogsAndPropagatesEnvironmentOnWindows()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        await using var sandbox = await ProcessTestSandbox.CreateAsync();
        var manager = CreateManager();

        try
        {
            var result = await manager.StartAsync(sandbox.CreateStartOptions(), CancellationToken.None);
            sandbox.TrackProcess(result.SupervisorProcessId);
            sandbox.TrackProcess(result.ProcessId);

            await WaitUntilAsync(
                () => FileContains(sandbox.InvocationLog, "serve")
                    && FileHasContent(sandbox.EnvironmentLog)
                    && FileHasContent(result.StandardOutputLog)
                    && FileHasContent(result.StandardErrorLog),
                "the target invocation, inherited environment, and redirected logs");

            var targetProcessId = Assert.IsType<int>(result.ProcessId);
            Assert.True(result.Started);
            Assert.NotEqual(result.SupervisorProcessId, targetProcessId);
            Assert.Equal(Path.GetFullPath(sandbox.FakeExecutable), result.Executable, ignoreCase: true);
            Assert.True(File.Exists(result.StateFile));
            Assert.Contains("fake stdout serve", await ReadSharedTextAsync(result.StandardOutputLog));
            Assert.Contains("fake stderr serve", await ReadSharedTextAsync(result.StandardErrorLog));
            Assert.Equal(["serve"], await File.ReadAllLinesAsync(sandbox.InvocationLog));
            var environmentLog = await File.ReadAllTextAsync(sandbox.EnvironmentLog);
            Assert.Contains($"OLLAMACTL_TEST_MARKER={TestEnvironmentValue}", environmentLog);
            Assert.Contains("OLLAMA_HOST=127.0.0.1:19434", environmentLog);

            await using var stateStream = File.OpenRead(result.StateFile);
            using var state = await JsonDocument.ParseAsync(stateStream);
            var root = state.RootElement;
            Assert.Equal(1, root.GetProperty("schemaVersion").GetInt32());
            Assert.Equal("running", root.GetProperty("status").GetString());
            Assert.Equal(result.SupervisorProcessId, root.GetProperty("supervisorProcessId").GetInt32());
            Assert.Equal(targetProcessId, root.GetProperty("targetProcessId").GetInt32());
            Assert.True(File.Exists(root.GetProperty("supervisorExecutable").GetString()));
            Assert.True(File.Exists(root.GetProperty("targetExecutable").GetString()));
            Assert.NotEqual(default, root.GetProperty("supervisorStartedAtUtc").GetDateTimeOffset());
            Assert.NotEqual(default, root.GetProperty("targetStartedAtUtc").GetDateTimeOffset());
            Assert.NotEqual(default, root.GetProperty("createdAtUtc").GetDateTimeOffset());
            Assert.Equal(JsonValueKind.Null, root.GetProperty("exitedAtUtc").ValueKind);
            Assert.Equal(JsonValueKind.Null, root.GetProperty("exitCode").ValueKind);

            var status = await manager.GetStatusAsync(sandbox.ConfigDirectory, CancellationToken.None);
            Assert.Equal(ManagedServerState.Running, status.State);
            Assert.Equal(result.SupervisorProcessId, status.SupervisorProcessId);
            Assert.Equal(targetProcessId, status.ProcessId);
        }
        finally
        {
            await manager.StopAsync(sandbox.ConfigDirectory, CancellationToken.None);
        }
    }

    [Fact]
    public async Task ConcurrentStartsReturnOneManagedProcessAndInvokeTargetOnceOnWindows()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        await using var sandbox = await ProcessTestSandbox.CreateAsync();
        var manager = CreateManager();

        try
        {
            var results = await Task.WhenAll(
                manager.StartAsync(sandbox.CreateStartOptions(), CancellationToken.None),
                manager.StartAsync(sandbox.CreateStartOptions(), CancellationToken.None));

            foreach (var result in results)
            {
                sandbox.TrackProcess(result.SupervisorProcessId);
                sandbox.TrackProcess(result.ProcessId);
            }

            await WaitUntilAsync(
                () => FileContains(sandbox.InvocationLog, "serve"),
                "the fake executable invocation");

            var started = Assert.Single(results, result => result.Started);
            var existing = Assert.Single(results, result => !result.Started);
            Assert.Equal(started.SupervisorProcessId, existing.SupervisorProcessId);
            Assert.Equal(started.ProcessId, existing.ProcessId);
            Assert.Equal(started.StateFile, existing.StateFile);
            Assert.Equal(started.StandardOutputLog, existing.StandardOutputLog);
            Assert.Equal(started.StandardErrorLog, existing.StandardErrorLog);
            Assert.Equal(["serve"], await File.ReadAllLinesAsync(sandbox.InvocationLog));

            using var supervisor = Process.GetProcessById(started.SupervisorProcessId);
            using var target = Process.GetProcessById(Assert.IsType<int>(started.ProcessId));
            Assert.False(supervisor.HasExited);
            Assert.False(target.HasExited);
        }
        finally
        {
            await manager.StopAsync(sandbox.ConfigDirectory, CancellationToken.None);
        }
    }

    [Fact]
    public async Task StopKillsManagedTreeRemovesStateAndLeavesUnrelatedProcessRunningOnWindows()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        await using var sandbox = await ProcessTestSandbox.CreateAsync();
        var manager = CreateManager();
        var unrelatedProcess = sandbox.StartUnmanagedFakeProcess("unrelated");

        try
        {
            await WaitUntilAsync(
                () => FileContains(sandbox.InvocationLog, "unrelated"),
                "the unrelated fake process");

            var start = await manager.StartAsync(sandbox.CreateStartOptions(), CancellationToken.None);
            sandbox.TrackProcess(start.SupervisorProcessId);
            sandbox.TrackProcess(start.ProcessId);
            var targetProcessId = Assert.IsType<int>(start.ProcessId);
            await WaitUntilAsync(
                () => FileContains(sandbox.InvocationLog, "serve"),
                "the managed fake process");

            using var supervisor = Process.GetProcessById(start.SupervisorProcessId);
            using var target = Process.GetProcessById(targetProcessId);
            var stop = await manager.StopAsync(sandbox.ConfigDirectory, CancellationToken.None);

            Assert.True(stop.Stopped);
            Assert.Equal(targetProcessId, stop.ProcessId);
            Assert.True(supervisor.HasExited);
            Assert.True(target.HasExited);
            Assert.False(File.Exists(start.StateFile));
            unrelatedProcess.Refresh();
            Assert.False(unrelatedProcess.HasExited);
            Assert.NotEqual(unrelatedProcess.Id, start.SupervisorProcessId);
            Assert.NotEqual(unrelatedProcess.Id, targetProcessId);

            var status = await manager.GetStatusAsync(sandbox.ConfigDirectory, CancellationToken.None);
            Assert.Equal(ManagedServerState.NotManaged, status.State);
        }
        finally
        {
            await manager.StopAsync(sandbox.ConfigDirectory, CancellationToken.None);
        }
    }

    [Fact]
    public async Task StaleOwnershipIsReportedAndStopNeverKillsForgedProcessOnWindows()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        await using var sandbox = await ProcessTestSandbox.CreateAsync();
        var manager = CreateManager();
        var unrelatedProcess = sandbox.StartUnmanagedFakeProcess("forged-state-target");
        await WaitUntilAsync(
            () => FileContains(sandbox.InvocationLog, "forged-state-target"),
            "the unrelated process used by the forged state");

        var runDirectory = Path.Combine(sandbox.ConfigDirectory, "run");
        var stateFile = Path.Combine(runDirectory, "ollamactl-server.json");
        Directory.CreateDirectory(runDirectory);
        var forgedState = JsonSerializer.Serialize(new
        {
            schemaVersion = 1,
            status = "running",
            supervisorProcessId = unrelatedProcess.Id,
            supervisorExecutable = sandbox.FakeExecutable,
            supervisorStartedAtUtc = unrelatedProcess.StartTime.ToUniversalTime(),
            targetProcessId = unrelatedProcess.Id,
            targetExecutable = sandbox.FakeExecutable,
            targetStartedAtUtc = unrelatedProcess.StartTime.ToUniversalTime(),
            createdAtUtc = DateTimeOffset.UtcNow,
            exitedAtUtc = (DateTimeOffset?)null,
            exitCode = (int?)null,
        });
        await File.WriteAllTextAsync(stateFile, forgedState);

        var status = await manager.GetStatusAsync(sandbox.ConfigDirectory, CancellationToken.None);
        Assert.Equal(ManagedServerState.Stale, status.State);
        Assert.Equal(unrelatedProcess.Id, status.SupervisorProcessId);
        Assert.Equal(unrelatedProcess.Id, status.ProcessId);

        var result = await manager.StopAsync(sandbox.ConfigDirectory, CancellationToken.None);

        Assert.False(result.Stopped);
        Assert.Equal(unrelatedProcess.Id, result.ProcessId);
        Assert.Contains("stale", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(File.Exists(stateFile));
        unrelatedProcess.Refresh();
        Assert.False(unrelatedProcess.HasExited);
    }

    private static OllamaServerManager CreateManager() => new(
        new OllamactlPathResolver(),
        ResolveCliLaunchInfo());

    private static ApplicationLaunchInfo ResolveCliLaunchInfo()
    {
        var repositoryRoot = FindRepositoryRoot();
        var configuration = FindBuildConfiguration();
        var executable = Path.Combine(
            repositoryRoot,
            "src",
            "Ollamactl.Cli",
            "bin",
            configuration,
            "net10.0",
            "ollamactl.exe");

        if (!File.Exists(executable))
        {
            throw new FileNotFoundException(
                "Build src/Ollamactl.Cli before running the server-manager integration tests.",
                executable);
        }

        executable = Path.GetFullPath(executable);
        return new ApplicationLaunchInfo(executable, [], executable);
    }

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Ollamactl.slnx")))
            {
                return directory.FullName;
            }
        }

        throw new DirectoryNotFoundException("Could not locate the ollamactl repository root.");
    }

    private static string FindBuildConfiguration()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory?.Parent is not null;
             directory = directory.Parent)
        {
            if (string.Equals(directory.Parent.Name, "bin", StringComparison.OrdinalIgnoreCase))
            {
                return directory.Name;
            }
        }

        throw new DirectoryNotFoundException("Could not determine the test build configuration.");
    }

    private static bool FileHasContent(string path) =>
        File.Exists(path) && new FileInfo(path).Length > 0;

    private static bool FileContains(string path, string expectedLine)
    {
        try
        {
            return File.Exists(path)
                && File.ReadLines(path).Contains(expectedLine, StringComparer.Ordinal);
        }
        catch (IOException)
        {
            return false;
        }
    }

    private static async Task<string> ReadSharedTextAsync(string path)
    {
        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete,
            bufferSize: 4096,
            useAsync: true);
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return await reader.ReadToEndAsync(CancellationToken.None);
    }

    private static async Task WaitUntilAsync(Func<bool> condition, string description)
    {
        var stopwatch = Stopwatch.StartNew();
        while (stopwatch.Elapsed < ReadinessTimeout)
        {
            if (condition())
            {
                return;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(50));
        }

        throw new TimeoutException($"Timed out waiting for {description}.");
    }

    private sealed class ProcessTestSandbox : IAsyncDisposable
    {
        private readonly List<Process> trackedProcesses = [];

        private ProcessTestSandbox(string rootDirectory)
        {
            RootDirectory = rootDirectory;
            ConfigDirectory = Path.Combine(rootDirectory, "config");
            var fakeDirectory = Path.Combine(rootDirectory, "fake");
            Directory.CreateDirectory(ConfigDirectory);
            Directory.CreateDirectory(fakeDirectory);
            FakeExecutable = Path.Combine(fakeDirectory, "fake-ollama.cmd");
            InvocationLog = Path.Combine(fakeDirectory, "invocations.log");
            EnvironmentLog = Path.Combine(fakeDirectory, "environment.log");
        }

        public string RootDirectory { get; }

        public string ConfigDirectory { get; }

        public string FakeExecutable { get; }

        public string InvocationLog { get; }

        public string EnvironmentLog { get; }

        public static async Task<ProcessTestSandbox> CreateAsync()
        {
            var rootDirectory = Path.Combine(
                Path.GetTempPath(),
                "ollamactl-integration-tests",
                Guid.NewGuid().ToString("N"));
            var sandbox = new ProcessTestSandbox(rootDirectory);
            await File.WriteAllTextAsync(
                sandbox.FakeExecutable,
                CreateFakeExecutableScript(),
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                CancellationToken.None);
            return sandbox;
        }

        public ServerStartOptions CreateStartOptions() => new(
            ConfigDirectory,
            FakeExecutable,
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["OLLAMACTL_TEST_MARKER"] = TestEnvironmentValue,
                ["OLLAMA_HOST"] = "127.0.0.1:19434",
            });

        public void TrackProcess(int? processId)
        {
            if (processId is null)
            {
                return;
            }

            try
            {
                trackedProcesses.Add(Process.GetProcessById(processId.Value));
            }
            catch (ArgumentException)
            {
                // A process that already exited requires no cleanup.
            }
        }

        public Process StartUnmanagedFakeProcess(string argument)
        {
            var windowsPowerShell = Path.Combine(
                Environment.SystemDirectory,
                "WindowsPowerShell",
                "v1.0",
                "powershell.exe");
            var escapedExecutable = FakeExecutable.Replace("'", "''", StringComparison.Ordinal);
            var command = $"& '{escapedExecutable}' {argument}";
            var encodedCommand = Convert.ToBase64String(Encoding.Unicode.GetBytes(command));
            var startInfo = new ProcessStartInfo
            {
                FileName = windowsPowerShell,
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = Path.GetDirectoryName(FakeExecutable)!,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            startInfo.ArgumentList.Add("-NoLogo");
            startInfo.ArgumentList.Add("-NoProfile");
            startInfo.ArgumentList.Add("-NonInteractive");
            startInfo.ArgumentList.Add("-EncodedCommand");
            startInfo.ArgumentList.Add(encodedCommand);

            var process = Process.Start(startInfo)
                ?? throw new InvalidOperationException("The unrelated fake process could not be started.");
            trackedProcesses.Add(process);
            return process;
        }

        public async ValueTask DisposeAsync()
        {
            foreach (var process in trackedProcesses)
            {
                await TerminateProcessAsync(process);
                process.Dispose();
            }

            await DeleteDirectoryWithRetryAsync(RootDirectory);
            GC.SuppressFinalize(this);
        }

        private static string CreateFakeExecutableScript() =>
            string.Join(
                Environment.NewLine,
                "@echo off",
                "setlocal",
                ">> \"%~dp0invocations.log\" echo %*",
                ">> \"%~dp0environment.log\" echo OLLAMACTL_TEST_MARKER=%OLLAMACTL_TEST_MARKER%",
                ">> \"%~dp0environment.log\" echo OLLAMA_HOST=%OLLAMA_HOST%",
                "echo fake stdout %*",
                "1>&2 echo fake stderr %*",
                ":wait",
                "\"%SystemRoot%\\System32\\ping.exe\" -n 2 127.0.0.1 >nul",
                "goto wait",
                string.Empty);

        private static async Task TerminateProcessAsync(Process process)
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }
            }
            catch (InvalidOperationException)
            {
                return;
            }
            catch (Win32Exception)
            {
                return;
            }

            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            try
            {
                await process.WaitForExitAsync(timeout.Token);
            }
            catch (InvalidOperationException)
            {
                // The process exited before the asynchronous wait was registered.
            }
            catch (OperationCanceledException)
            {
                // A best-effort kill was already issued; directory cleanup is retried below.
            }
        }

        private static async Task DeleteDirectoryWithRetryAsync(string path)
        {
            for (var attempt = 0; attempt < 10; attempt++)
            {
                try
                {
                    if (Directory.Exists(path))
                    {
                        Directory.Delete(path, recursive: true);
                    }

                    return;
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
        }
    }
}
