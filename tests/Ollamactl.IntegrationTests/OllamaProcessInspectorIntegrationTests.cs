using System.ComponentModel;
using System.Diagnostics;
using Ollamactl.Application.Processes;
using Ollamactl.Infrastructure.Processes;
using Xunit;

namespace Ollamactl.IntegrationTests;

[Trait("Category", "Integration")]
public sealed class OllamaProcessInspectorIntegrationTests
{
    [Fact]
    public async Task InspectFindsAnOllamaPrefixedProcessWithRuntimeMetrics()
    {
        await using var sandbox = await OllamaProcessSandbox.CreateAsync();
        var process = sandbox.Start();
        var inspector = new OllamaProcessInspector();

        var inspected = await WaitForProcessAsync(inspector, process.Id);

        Assert.Equal(process.Id, inspected.ProcessId);
        Assert.StartsWith("ollama", inspected.Name, StringComparison.OrdinalIgnoreCase);
        Assert.NotNull(inspected.ExecutablePath);
        Assert.Equal(
            Path.GetFullPath(sandbox.ExecutablePath),
            Path.GetFullPath(inspected.ExecutablePath!),
            ignoreCase: OperatingSystem.IsWindows());
        Assert.NotNull(inspected.StartedAtUtc);
        Assert.Equal(TimeSpan.Zero, inspected.StartedAtUtc!.Value.Offset);
        Assert.NotNull(inspected.CpuTime);
        Assert.True(inspected.CpuTime >= TimeSpan.Zero);
        Assert.NotNull(inspected.WorkingSetBytes);
        Assert.True(inspected.WorkingSetBytes > 0);
    }

    private static async Task<OllamaProcessInfo> WaitForProcessAsync(
        OllamaProcessInspector inspector,
        int processId)
    {
        var stopwatch = Stopwatch.StartNew();
        while (stopwatch.Elapsed < TimeSpan.FromSeconds(5))
        {
            var processes = await inspector.InspectAsync(CancellationToken.None);
            var inspected = processes.SingleOrDefault(item => item.ProcessId == processId);
            if (inspected is not null)
            {
                return inspected;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(50));
        }

        throw new TimeoutException($"Timed out waiting for process {processId} to be inspected.");
    }

    private sealed class OllamaProcessSandbox : IAsyncDisposable
    {
        private Process? process;

        private OllamaProcessSandbox(string rootDirectory, string executablePath)
        {
            RootDirectory = rootDirectory;
            ExecutablePath = executablePath;
        }

        public string RootDirectory { get; }

        public string ExecutablePath { get; }

        public static Task<OllamaProcessSandbox> CreateAsync()
        {
            var rootDirectory = Path.Combine(
                Path.GetTempPath(),
                "ollamactl-process-inspector-tests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(rootDirectory);

            var sourceExecutable = OperatingSystem.IsWindows()
                ? Environment.GetEnvironmentVariable("ComSpec")
                    ?? Path.Combine(Environment.SystemDirectory, "cmd.exe")
                : "/bin/sh";
            var executablePath = Path.Combine(
                rootDirectory,
                OperatingSystem.IsWindows() ? "ollama-watch.exe" : "ollama-watch");
            File.Copy(sourceExecutable, executablePath);
            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(
                    executablePath,
                    UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            }

            return Task.FromResult(new OllamaProcessSandbox(rootDirectory, executablePath));
        }

        public Process Start()
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = ExecutablePath,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };

            if (OperatingSystem.IsWindows())
            {
                startInfo.ArgumentList.Add("/d");
                startInfo.ArgumentList.Add("/c");
                startInfo.ArgumentList.Add("ping -n 31 127.0.0.1 > nul");
            }
            else
            {
                startInfo.ArgumentList.Add("-c");
                startInfo.ArgumentList.Add("while :; do sleep 1; done");
            }

            process = Process.Start(startInfo)
                ?? throw new InvalidOperationException("The test process could not be started.");
            return process;
        }

        public async ValueTask DisposeAsync()
        {
            if (process is not null)
            {
                await TerminateProcessAsync(process);
                process.Dispose();
            }

            await DeleteDirectoryWithRetryAsync(RootDirectory);
            GC.SuppressFinalize(this);
        }

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
                // The kill is best-effort; directory deletion below still retries.
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
