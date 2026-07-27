using System.Diagnostics;
using System.Text;

namespace Ollamactl.Infrastructure.Processes;

public static class OllamaSupervisorEntryPoint
{
    public const string CommandName = "__supervise";

    public static bool IsInvocation(IReadOnlyList<string> arguments) =>
        arguments.Count > 0 && string.Equals(arguments[0], CommandName, StringComparison.Ordinal);

    public static async Task<int> RunAsync(
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        if (!IsInvocation(arguments))
        {
            throw new ArgumentException("The supervisor command marker is missing.", nameof(arguments));
        }

        var stateFile = GetRequiredOption(arguments, "--state-file");
        var standardOutputLog = GetRequiredOption(arguments, "--stdout-log");
        var standardErrorLog = GetRequiredOption(arguments, "--stderr-log");
        var targetExecutable = Path.GetFullPath(GetRequiredOption(arguments, "--target"));

        Directory.CreateDirectory(Path.GetDirectoryName(standardOutputLog)!);
        Directory.CreateDirectory(Path.GetDirectoryName(standardErrorLog)!);

        ServerProcessState? state = null;
        try
        {
            using (var supervisor = Process.GetCurrentProcess())
            {
                state = new ServerProcessState(
                    ServerProcessState.CurrentSchemaVersion,
                    ServerProcessState.StartingStatus,
                    supervisor.Id,
                    TryGetExecutable(supervisor)
                        ?? Path.GetFullPath(Environment.ProcessPath
                            ?? throw new InvalidOperationException(
                                "Unable to determine the supervisor executable.")),
                    supervisor.StartTime.ToUniversalTime(),
                    TargetProcessId: null,
                    targetExecutable,
                    TargetStartedAtUtc: null,
                    DateTimeOffset.UtcNow,
                    ExitedAtUtc: null,
                    ExitCode: null);
            }

            await ServerStateStore.WriteAsync(stateFile, state, cancellationToken).ConfigureAwait(false);
            var startInfo = CreateTargetStartInfo(targetExecutable);
            using var targetProcess = Process.Start(startInfo)
                ?? throw new InvalidOperationException("The Ollama process could not be started.");

            var targetStartedAt = targetProcess.StartTime.ToUniversalTime();
            var runtimeExecutable = TryGetExecutable(targetProcess)
                ?? Path.GetFullPath(startInfo.FileName);
            state = state with
            {
                Status = ServerProcessState.RunningStatus,
                TargetProcessId = targetProcess.Id,
                TargetExecutable = runtimeExecutable,
                TargetStartedAtUtc = targetStartedAt,
            };
            await ServerStateStore.WriteAsync(stateFile, state, cancellationToken).ConfigureAwait(false);

            await using var outputStream = OpenAppendStream(standardOutputLog);
            await using var errorStream = OpenAppendStream(standardErrorLog);
            var copyOutput = targetProcess.StandardOutput.BaseStream.CopyToAsync(outputStream, cancellationToken);
            var copyError = targetProcess.StandardError.BaseStream.CopyToAsync(errorStream, cancellationToken);

            await targetProcess.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            await Task.WhenAll(copyOutput, copyError).ConfigureAwait(false);
            await outputStream.FlushAsync(cancellationToken).ConfigureAwait(false);
            await errorStream.FlushAsync(cancellationToken).ConfigureAwait(false);

            state = state with
            {
                Status = ServerProcessState.ExitedStatus,
                ExitedAtUtc = DateTimeOffset.UtcNow,
                ExitCode = targetProcess.ExitCode,
            };
            await ServerStateStore.WriteAsync(stateFile, state, CancellationToken.None).ConfigureAwait(false);
            return targetProcess.ExitCode;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            await TryAppendSupervisorErrorAsync(standardErrorLog, exception).ConfigureAwait(false);
            if (state is not null)
            {
                state = state with
                {
                    Status = ServerProcessState.ExitedStatus,
                    ExitedAtUtc = DateTimeOffset.UtcNow,
                    ExitCode = -1,
                };
                await TryWriteStateAsync(stateFile, state).ConfigureAwait(false);
            }

            return -1;
        }
    }

    private static ProcessStartInfo CreateTargetStartInfo(string targetExecutable)
    {
        var fullTargetPath = Path.GetFullPath(targetExecutable);
        ProcessStartInfo startInfo;

        var extension = Path.GetExtension(fullTargetPath);
        if (OperatingSystem.IsWindows()
            && (string.Equals(extension, ".cmd", StringComparison.OrdinalIgnoreCase)
                || string.Equals(extension, ".bat", StringComparison.OrdinalIgnoreCase)))
        {
            var commandInterpreter = Environment.GetEnvironmentVariable("ComSpec")
                ?? Path.Combine(Environment.SystemDirectory, "cmd.exe");
            startInfo = new ProcessStartInfo
            {
                FileName = commandInterpreter,
                WorkingDirectory = Path.GetDirectoryName(fullTargetPath)!,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            startInfo.ArgumentList.Add("/d");
            startInfo.ArgumentList.Add("/s");
            startInfo.ArgumentList.Add("/c");
            startInfo.ArgumentList.Add("call");
            startInfo.ArgumentList.Add(fullTargetPath);
            startInfo.ArgumentList.Add("serve");
            return startInfo;
        }

        startInfo = new ProcessStartInfo
        {
            FileName = fullTargetPath,
            WorkingDirectory = Path.GetDirectoryName(fullTargetPath)!,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        startInfo.ArgumentList.Add("serve");
        return startInfo;
    }

    private static FileStream OpenAppendStream(string path) => new(
        path,
        FileMode.Append,
        FileAccess.Write,
        FileShare.ReadWrite | FileShare.Delete,
        // Keep server diagnostics observable while the long-running process is
        // alive. A buffered destination would retain small log writes until
        // shutdown, defeating `server logs` and live health diagnostics.
        bufferSize: 1,
        useAsync: true);

    private static async Task TryAppendSupervisorErrorAsync(string errorLog, Exception exception)
    {
        try
        {
            var message = $"[{DateTimeOffset.UtcNow:O}] supervisor error: {exception}{Environment.NewLine}";
            await File.AppendAllTextAsync(errorLog, message, new UTF8Encoding(false)).ConfigureAwait(false);
        }
        catch (Exception appendException) when (appendException is IOException or UnauthorizedAccessException)
        {
        }
    }

    private static async Task TryWriteStateAsync(string stateFile, ServerProcessState state)
    {
        try
        {
            await ServerStateStore.WriteAsync(stateFile, state, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }

    private static string GetRequiredOption(IReadOnlyList<string> arguments, string option)
    {
        for (var index = 1; index < arguments.Count - 1; index++)
        {
            if (string.Equals(arguments[index], option, StringComparison.Ordinal))
            {
                return arguments[index + 1];
            }
        }

        throw new ArgumentException($"Missing internal supervisor option {option}.", nameof(arguments));
    }

    private static string? TryGetExecutable(Process process)
    {
        try
        {
            return process.MainModule?.FileName is { } path ? Path.GetFullPath(path) : null;
        }
        catch (Exception exception) when (exception is
            System.ComponentModel.Win32Exception or
            InvalidOperationException or
            NotSupportedException or
            UnauthorizedAccessException)
        {
            return null;
        }
    }
}
