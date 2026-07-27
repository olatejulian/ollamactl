using System.Diagnostics;

namespace Ollamactl.EndToEndTests;

internal sealed record CliProcessResult(int ExitCode, string StandardOutput, string StandardError);

internal sealed record CliProcessRunOptions(
    string? ConfigDirectory = null,
    IReadOnlyDictionary<string, string?>? EnvironmentVariables = null,
    TimeSpan? Timeout = null);

internal static class CliProcessRunner
{
    private static readonly TimeSpan ProcessTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan CleanupRetryDelay = TimeSpan.FromMilliseconds(100);
    private const int CleanupAttempts = 10;

    public static Task<CliProcessResult> RunAsync(params string[] arguments) =>
        RunAsync(new CliProcessRunOptions(), arguments);

    public static async Task<CliProcessResult> RunAsync(
        CliProcessRunOptions options,
        params string[] arguments)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(arguments);

        var ownsConfigDirectory = string.IsNullOrWhiteSpace(options.ConfigDirectory);
        var configDirectory = ownsConfigDirectory
            ? CreateOwnedConfigDirectoryPath()
            : Path.GetFullPath(options.ConfigDirectory!);
        Directory.CreateDirectory(configDirectory);

        try
        {
            var startInfo = CreateStartInfo(arguments, configDirectory, options.EnvironmentVariables);
            using var process = new Process { StartInfo = startInfo };
            if (!process.Start())
            {
                throw new InvalidOperationException($"Failed to start the CLI process '{startInfo.FileName}'.");
            }

            var standardOutputTask = process.StandardOutput.ReadToEndAsync();
            var standardErrorTask = process.StandardError.ReadToEndAsync();
            var processTimeout = options.Timeout ?? ProcessTimeout;
            if (processTimeout <= TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(options),
                    processTimeout,
                    "The CLI process timeout must be greater than zero.");
            }

            using var timeout = new CancellationTokenSource(processTimeout);

            try
            {
                await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException exception) when (timeout.IsCancellationRequested)
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                    await process.WaitForExitAsync().ConfigureAwait(false);
                }

                var timedOutOutput = await standardOutputTask.ConfigureAwait(false);
                var timedOutError = await standardErrorTask.ConfigureAwait(false);
                throw new TimeoutException(
                    $"The CLI did not exit within {processTimeout}. stdout: {timedOutOutput}; stderr: {timedOutError}",
                    exception);
            }

            var standardOutput = await standardOutputTask.ConfigureAwait(false);
            var standardError = await standardErrorTask.ConfigureAwait(false);
            return new CliProcessResult(process.ExitCode, standardOutput, standardError);
        }
        finally
        {
            if (ownsConfigDirectory)
            {
                await DeleteDirectoryWithRetryAsync(configDirectory).ConfigureAwait(false);
            }
        }
    }

    private static ProcessStartInfo CreateStartInfo(
        IEnumerable<string> arguments,
        string configDirectory,
        IReadOnlyDictionary<string, string?>? environmentVariables)
    {
        var cli = ResolveCli();
        var startInfo = new ProcessStartInfo
        {
            FileName = cli.FileName,
            WorkingDirectory = configDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };

        foreach (var prefixArgument in cli.PrefixArguments)
        {
            startInfo.ArgumentList.Add(prefixArgument);
        }

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        startInfo.Environment["OLLAMACTL_CONFIG_DIR"] = configDirectory;
        startInfo.Environment["OLLAMACTL_TIMEOUT_SECONDS"] = "5";
        startInfo.Environment["OLLAMA_HOST"] = "http://127.0.0.1:1";
        startInfo.Environment["NO_COLOR"] = "1";
        startInfo.Environment["DOTNET_SYSTEM_GLOBALIZATION_INVARIANT"] = "1";
        startInfo.Environment["DOTNET_CLI_UI_LANGUAGE"] = "en-US";
        startInfo.Environment["LANG"] = "C";
        startInfo.Environment["LANGUAGE"] = "en";
        startInfo.Environment["LC_ALL"] = "C";
        startInfo.Environment["TZ"] = "UTC";

        foreach (var proxyVariable in new[]
                 {
                     "HTTP_PROXY",
                     "HTTPS_PROXY",
                     "ALL_PROXY",
                     "http_proxy",
                     "https_proxy",
                     "all_proxy",
                 })
        {
            startInfo.Environment.Remove(proxyVariable);
        }

        startInfo.Environment["NO_PROXY"] = "localhost,127.0.0.1,::1";
        startInfo.Environment["no_proxy"] = "localhost,127.0.0.1,::1";

        if (environmentVariables is not null)
        {
            foreach (var variable in environmentVariables)
            {
                if (string.IsNullOrWhiteSpace(variable.Key))
                {
                    throw new ArgumentException(
                        "Environment variable names cannot be empty.",
                        nameof(environmentVariables));
                }

                if (variable.Value is null)
                {
                    startInfo.Environment.Remove(variable.Key);
                }
                else
                {
                    startInfo.Environment[variable.Key] = variable.Value;
                }
            }
        }

        return startInfo;
    }

    private static string CreateOwnedConfigDirectoryPath() => Path.Combine(
        Path.GetTempPath(),
        $"ollamactl-e2e-{Guid.NewGuid().ToString("N", System.Globalization.CultureInfo.InvariantCulture)}");

    private static CliLaunch ResolveCli()
    {
        var explicitExecutable = Environment.GetEnvironmentVariable("OLLAMACTL_E2E_EXECUTABLE");
        if (!string.IsNullOrWhiteSpace(explicitExecutable))
        {
            var fullPath = Path.GetFullPath(explicitExecutable);
            if (!File.Exists(fullPath))
            {
                throw new FileNotFoundException(
                    "OLLAMACTL_E2E_EXECUTABLE does not point to a file.",
                    fullPath);
            }

            if (OperatingSystem.IsWindows()
                && !string.Equals(Path.GetExtension(fullPath), ".exe", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    "OLLAMACTL_E2E_EXECUTABLE must point to a Windows .exe file.");
            }

            if (!OperatingSystem.IsWindows())
            {
                var mode = File.GetUnixFileMode(fullPath);
                const UnixFileMode executeBits =
                    UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute;
                if ((mode & executeBits) == 0)
                {
                    throw new UnauthorizedAccessException(
                        "OLLAMACTL_E2E_EXECUTABLE must point to an executable file.");
                }
            }

            return new CliLaunch(fullPath, []);
        }

        var testOutputDirectory = new DirectoryInfo(AppContext.BaseDirectory);
        var targetFramework = testOutputDirectory.Name;
        var configuration = testOutputDirectory.Parent?.Name
            ?? throw new InvalidOperationException("Could not determine the test build configuration.");
        var repositoryRoot = FindRepositoryRoot(testOutputDirectory);
        var cliOutputDirectory = Path.Combine(
            repositoryRoot.FullName,
            "src",
            "Ollamactl.Cli",
            "bin",
            configuration,
            targetFramework);

        var appHost = Path.Combine(
            cliOutputDirectory,
            OperatingSystem.IsWindows() ? "ollamactl.exe" : "ollamactl");
        if (File.Exists(appHost))
        {
            return new CliLaunch(appHost, []);
        }

        var cliAssembly = Path.Combine(cliOutputDirectory, "ollamactl.dll");
        if (File.Exists(cliAssembly))
        {
            return new CliLaunch("dotnet", [cliAssembly]);
        }

        throw new FileNotFoundException(
            $"Could not find the built ollamactl apphost or DLL in '{cliOutputDirectory}'.",
            appHost);
    }

    private static DirectoryInfo FindRepositoryRoot(DirectoryInfo startingDirectory)
    {
        for (var current = startingDirectory; current is not null; current = current.Parent)
        {
            if (File.Exists(Path.Combine(current.FullName, "ollamactl.slnx")))
            {
                return current;
            }
        }

        throw new DirectoryNotFoundException(
            $"Could not locate ollamactl.slnx above '{startingDirectory.FullName}'.");
    }

    private static async Task DeleteDirectoryWithRetryAsync(string directory)
    {
        for (var attempt = 1; attempt <= CleanupAttempts; attempt++)
        {
            try
            {
                Directory.Delete(directory, recursive: true);
                return;
            }
            catch (DirectoryNotFoundException)
            {
                return;
            }
            catch (IOException) when (attempt < CleanupAttempts)
            {
                await Task.Delay(CleanupRetryDelay).ConfigureAwait(false);
            }
            catch (UnauthorizedAccessException) when (attempt < CleanupAttempts)
            {
                await Task.Delay(CleanupRetryDelay).ConfigureAwait(false);
            }
        }
    }

    private sealed record CliLaunch(string FileName, string[] PrefixArguments);
}
