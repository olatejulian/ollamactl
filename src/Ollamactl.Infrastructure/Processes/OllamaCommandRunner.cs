using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using Ollamactl.Application.Abstractions;
using Ollamactl.Application.Execution;

namespace Ollamactl.Infrastructure.Processes;

public sealed class OllamaCommandRunner : IOllamaCommandRunner
{
    public async Task<OllamaCommandResult> RunAsync(
        OllamaCommandRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Executable);
        ArgumentNullException.ThrowIfNull(request.Arguments);
        ArgumentNullException.ThrowIfNull(request.Environment);
        cancellationToken.ThrowIfCancellationRequested();

        if (request.Timeout is { } timeout && timeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(request),
                request.Timeout,
                "The command timeout must be greater than zero.");
        }

        var startInfo = CreateStartInfo(request);
        using var process = new Process { StartInfo = startInfo };
        if (!process.Start())
        {
            throw new InvalidOperationException("The Ollama command could not be started.");
        }

        var standardOutputTask = request.Mode == OllamaCommandMode.Capture
            ? process.StandardOutput.ReadToEndAsync(CancellationToken.None)
            : null;
        var standardErrorTask = request.Mode == OllamaCommandMode.Capture
            ? process.StandardError.ReadToEndAsync(CancellationToken.None)
            : null;

        using var timeoutCancellation = request.Timeout is null
            ? null
            : new CancellationTokenSource(request.Timeout.Value);
        using var linkedCancellation = timeoutCancellation is null
            ? CancellationTokenSource.CreateLinkedTokenSource(cancellationToken)
            : CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                timeoutCancellation.Token);

        try
        {
            await process.WaitForExitAsync(linkedCancellation.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException exception)
        {
            await TerminateProcessTreeAsync(process).ConfigureAwait(false);
            await DrainOutputAsync(standardOutputTask, standardErrorTask).ConfigureAwait(false);

            if (cancellationToken.IsCancellationRequested)
            {
                throw new OperationCanceledException(
                    "The Ollama command was canceled.",
                    exception,
                    cancellationToken);
            }

            if (timeoutCancellation?.IsCancellationRequested == true)
            {
                throw new TimeoutException(
                    $"The Ollama command exceeded its timeout of {request.Timeout}.",
                    exception);
            }

            throw;
        }

        var standardOutput = standardOutputTask is null
            ? null
            : await standardOutputTask.ConfigureAwait(false);
        var standardError = standardErrorTask is null
            ? null
            : await standardErrorTask.ConfigureAwait(false);

        return new OllamaCommandResult(process.ExitCode, standardOutput, standardError);
    }

    private static ProcessStartInfo CreateStartInfo(OllamaCommandRequest request)
    {
        var capture = request.Mode switch
        {
            OllamaCommandMode.Capture => true,
            OllamaCommandMode.Foreground => false,
            _ => throw new ArgumentOutOfRangeException(
                nameof(request),
                request.Mode,
                "Unsupported Ollama command mode."),
        };

        var startInfo = new ProcessStartInfo
        {
            UseShellExecute = false,
            CreateNoWindow = capture,
            RedirectStandardInput = false,
            RedirectStandardOutput = capture,
            RedirectStandardError = capture,
        };

        if (!string.IsNullOrWhiteSpace(request.WorkingDirectory))
        {
            startInfo.WorkingDirectory = Path.GetFullPath(request.WorkingDirectory);
        }

        if (IsWindowsBatchFile(request.Executable))
        {
            startInfo.FileName = GetWindowsCommandInterpreter();
            // cmd.exe has its own parser: ArgumentList does not quote tokens
            // such as --flag=value, and CALL then splits them at '='. Pass a
            // single, fully quoted /c payload to preserve native arguments.
            startInfo.Arguments = "/d /s /c " + CreateBatchCommand(
                request.Executable,
                request.Arguments);
        }
        else
        {
            startInfo.FileName = request.Executable;
            foreach (var argument in request.Arguments)
            {
                startInfo.ArgumentList.Add(argument);
            }
        }

        startInfo.Environment.Clear();
        foreach (var variable in request.Environment)
        {
            if (string.IsNullOrWhiteSpace(variable.Key))
            {
                throw new ArgumentException(
                    "Environment variable names cannot be empty.",
                    nameof(request));
            }

            startInfo.Environment[variable.Key] = variable.Value;
        }

        return startInfo;
    }

    private static bool IsWindowsBatchFile(string executable) =>
        OperatingSystem.IsWindows()
        && (string.Equals(Path.GetExtension(executable), ".cmd", StringComparison.OrdinalIgnoreCase)
            || string.Equals(Path.GetExtension(executable), ".bat", StringComparison.OrdinalIgnoreCase));

    private static string GetWindowsCommandInterpreter()
    {
        var commandInterpreter = Environment.GetEnvironmentVariable("ComSpec");
        if (!string.IsNullOrWhiteSpace(commandInterpreter) && File.Exists(commandInterpreter))
        {
            return commandInterpreter;
        }

        var systemCommandInterpreter = Path.Combine(Environment.SystemDirectory, "cmd.exe");
        return File.Exists(systemCommandInterpreter)
            ? systemCommandInterpreter
            : throw new FileNotFoundException(
                "The Windows command interpreter was not found.",
                systemCommandInterpreter);
    }

    private static string CreateBatchCommand(
        string executable,
        IReadOnlyList<string> arguments)
    {
        var command = new StringBuilder();
        command.Append('"');
        AppendQuotedCommandPromptArgument(command, Path.GetFullPath(executable));
        foreach (var argument in arguments)
        {
            command.Append(' ');
            AppendQuotedCommandPromptArgument(command, argument);
        }

        command.Append('"');
        return command.ToString();
    }

    private static void AppendQuotedCommandPromptArgument(StringBuilder command, string value)
    {
        if (value.Contains('\0', StringComparison.Ordinal)
            || value.Contains('\r', StringComparison.Ordinal)
            || value.Contains('\n', StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "Batch-file arguments cannot contain null characters or line breaks.",
                nameof(value));
        }

        command.Append('"');
        command.Append(value.Replace("\"", "\"\"", StringComparison.Ordinal));
        command.Append('"');
    }

    private static async Task TerminateProcessTreeAsync(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException) when (HasExited(process))
        {
            return;
        }
        catch (Win32Exception) when (HasExited(process))
        {
            return;
        }

        if (!HasExited(process))
        {
            await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
        }
    }

    private static bool HasExited(Process process)
    {
        try
        {
            return process.HasExited;
        }
        catch (InvalidOperationException)
        {
            return true;
        }
    }

    private static async Task DrainOutputAsync(
        Task<string>? standardOutputTask,
        Task<string>? standardErrorTask)
    {
        try
        {
            if (standardOutputTask is not null)
            {
                await standardOutputTask.ConfigureAwait(false);
            }

            if (standardErrorTask is not null)
            {
                await standardErrorTask.ConfigureAwait(false);
            }
        }
        catch (IOException)
        {
            // The process tree is already terminated; redirected pipes may close abruptly.
        }
        catch (ObjectDisposedException)
        {
            // The process tree is already terminated; redirected pipes may close abruptly.
        }
    }
}
