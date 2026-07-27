using System.Diagnostics;
using Ollamactl.Application.Abstractions;
using Ollamactl.Application.Configuration;
using Ollamactl.Application.Server;
using Ollamactl.Infrastructure.Configuration;

namespace Ollamactl.Infrastructure.Processes;

public sealed class OllamaServerManager : IOllamaServerManager
{
    private static readonly TimeSpan LockTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan SupervisorStartupTimeout = TimeSpan.FromSeconds(10);
    private readonly IOllamactlPathResolver pathResolver;
    private readonly ApplicationLaunchInfo applicationLaunch;

    public OllamaServerManager()
        : this(
            new OllamactlPathResolver(),
            ApplicationLaunchInfo.ForCurrentProcess(Environment.GetCommandLineArgs().FirstOrDefault()))
    {
    }

    public OllamaServerManager(
        IOllamactlPathResolver pathResolver,
        ApplicationLaunchInfo applicationLaunch)
    {
        this.pathResolver = pathResolver ?? throw new ArgumentNullException(nameof(pathResolver));
        this.applicationLaunch = applicationLaunch ?? throw new ArgumentNullException(nameof(applicationLaunch));
    }

    public async Task<ServerStartResult> StartAsync(
        ServerStartOptions options,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.ConfigDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.OllamaExecutable);
        ArgumentNullException.ThrowIfNull(options.EnvironmentVariables);

        var executable = Path.GetFullPath(options.OllamaExecutable);
        if (!File.Exists(executable))
        {
            throw new FileNotFoundException("The Ollama executable was not found.", executable);
        }

        var paths = pathResolver.Resolve(options.ConfigDirectory);
        Directory.CreateDirectory(paths.LogDirectory);
        Directory.CreateDirectory(paths.RunDirectory);
        var lockFile = Path.Combine(paths.RunDirectory, "ollamactl-server.lock");

        await using var operationLock = await AcquireLockAsync(lockFile, cancellationToken).ConfigureAwait(false);
        var existingState = await ServerStateStore.ReadAsync(paths.ServerStateFile, cancellationToken)
            .ConfigureAwait(false);
        if (existingState is not null
            && (IsRunningState(existingState) || MayStillBeRunning(existingState)))
        {
            return new ServerStartResult(
                Started: false,
                existingState.SupervisorProcessId,
                existingState.TargetProcessId,
                existingState.TargetExecutable,
                paths.ServerStateFile,
                paths.StandardOutputLog,
                paths.StandardErrorLog);
        }

        ServerStateStore.Delete(paths.ServerStateFile);
        var startInfo = CreateSupervisorStartInfo(executable, paths);
        foreach (var variable in options.EnvironmentVariables)
        {
            startInfo.Environment[variable.Key] = variable.Value;
        }

        Process? supervisor = null;
        try
        {
            supervisor = Process.Start(startInfo)
                ?? throw new InvalidOperationException("The ollamactl supervisor could not be started.");
            var runningState = await WaitForRunningStateAsync(
                    paths.ServerStateFile,
                    paths.StandardErrorLog,
                    supervisor,
                    cancellationToken)
                .ConfigureAwait(false);

            return new ServerStartResult(
                Started: true,
                runningState.SupervisorProcessId,
                runningState.TargetProcessId,
                executable,
                paths.ServerStateFile,
                paths.StandardOutputLog,
                paths.StandardErrorLog);
        }
        catch
        {
            if (supervisor is not null)
            {
                await TerminateProcessTreeAsync(supervisor).ConfigureAwait(false);
            }

            ServerStateStore.Delete(paths.ServerStateFile);
            throw;
        }
        finally
        {
            supervisor?.Dispose();
        }
    }

    public async Task<ServerStopResult> StopAsync(
        string configDirectory,
        CancellationToken cancellationToken)
    {
        var paths = pathResolver.Resolve(configDirectory);
        Directory.CreateDirectory(paths.RunDirectory);
        var lockFile = Path.Combine(paths.RunDirectory, "ollamactl-server.lock");
        await using var operationLock = await AcquireLockAsync(lockFile, cancellationToken).ConfigureAwait(false);
        var state = await ServerStateStore.ReadAsync(paths.ServerStateFile, cancellationToken).ConfigureAwait(false);
        if (state is null)
        {
            ServerStateStore.Delete(paths.ServerStateFile);
            return new ServerStopResult(false, null, "No ollamactl-managed Ollama server is running.");
        }

        var stopped = false;
        var reportedProcessId = state.TargetProcessId ?? state.SupervisorProcessId;
        if (TryGetOwnedProcess(
                state.SupervisorProcessId,
                state.SupervisorStartedAtUtc,
                state.SupervisorExecutable,
                out var supervisor))
        {
            using (supervisor)
            {
                supervisor!.Kill(entireProcessTree: true);
                stopped = true;
                try
                {
                    await supervisor.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
                }
                finally
                {
                    ServerStateStore.Delete(paths.ServerStateFile);
                }
            }
        }
        else if (state.TargetProcessId is { } targetProcessId
                 && state.TargetStartedAtUtc is { } targetStartedAt
                 && TryGetOwnedProcess(
                     targetProcessId,
                     targetStartedAt,
                     state.TargetExecutable,
                     out var target))
        {
            using (target)
            {
                target!.Kill(entireProcessTree: true);
                stopped = true;
                try
                {
                    await target.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
                }
                finally
                {
                    ServerStateStore.Delete(paths.ServerStateFile);
                }
            }
        }
        else
        {
            ServerStateStore.Delete(paths.ServerStateFile);
        }

        return stopped
            ? new ServerStopResult(true, reportedProcessId, "Ollama server stopped.")
            : new ServerStopResult(false, reportedProcessId, "Removed stale server state; no process was stopped.");
    }

    public async Task<ManagedServerStatus> GetStatusAsync(
        string configDirectory,
        CancellationToken cancellationToken)
    {
        var paths = pathResolver.Resolve(configDirectory);
        var stateFileExists = File.Exists(paths.ServerStateFile);
        var state = await ServerStateStore.ReadAsync(paths.ServerStateFile, cancellationToken).ConfigureAwait(false);
        if (state is null)
        {
            return CreateStatus(
                stateFileExists ? ManagedServerState.Stale : ManagedServerState.NotManaged,
                null,
                paths);
        }

        if (string.Equals(state.Status, ServerProcessState.ExitedStatus, StringComparison.Ordinal))
        {
            return CreateStatus(ManagedServerState.Exited, state, paths);
        }

        var supervisorRunning = IsOwnedProcess(
            state.SupervisorProcessId,
            state.SupervisorStartedAtUtc,
            state.SupervisorExecutable);
        if (!supervisorRunning)
        {
            return CreateStatus(ManagedServerState.Stale, state, paths);
        }

        if (state.TargetProcessId is null || state.TargetStartedAtUtc is null)
        {
            return CreateStatus(ManagedServerState.Starting, state, paths);
        }

        var targetRunning = IsOwnedProcess(
            state.TargetProcessId.Value,
            state.TargetStartedAtUtc.Value,
            state.TargetExecutable);
        return CreateStatus(
            targetRunning ? ManagedServerState.Running : ManagedServerState.Stale,
            state,
            paths);
    }

    private ProcessStartInfo CreateSupervisorStartInfo(string executable, OllamactlPaths paths)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = applicationLaunch.FileName,
            WorkingDirectory = Path.GetDirectoryName(executable)!,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        foreach (var argument in applicationLaunch.PrefixArguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        startInfo.ArgumentList.Add(OllamaSupervisorEntryPoint.CommandName);
        startInfo.ArgumentList.Add("--state-file");
        startInfo.ArgumentList.Add(paths.ServerStateFile);
        startInfo.ArgumentList.Add("--stdout-log");
        startInfo.ArgumentList.Add(paths.StandardOutputLog);
        startInfo.ArgumentList.Add("--stderr-log");
        startInfo.ArgumentList.Add(paths.StandardErrorLog);
        startInfo.ArgumentList.Add("--target");
        startInfo.ArgumentList.Add(executable);
        return startInfo;
    }

    private static async Task<ServerProcessState> WaitForRunningStateAsync(
        string stateFile,
        string standardErrorLog,
        Process supervisor,
        CancellationToken cancellationToken)
    {
        var deadline = DateTimeOffset.UtcNow.Add(SupervisorStartupTimeout);
        while (DateTimeOffset.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            supervisor.Refresh();
            var state = await ServerStateStore.ReadAsync(stateFile, cancellationToken).ConfigureAwait(false);
            if (state is not null
                && string.Equals(state.Status, ServerProcessState.RunningStatus, StringComparison.Ordinal)
                && state.TargetProcessId is not null
                && state.TargetStartedAtUtc is not null)
            {
                return state;
            }

            if (state is not null
                && string.Equals(state.Status, ServerProcessState.ExitedStatus, StringComparison.Ordinal))
            {
                var diagnostic = state.ExitCode == -1
                    ? TryReadDiagnostic(standardErrorLog)
                    : null;
                throw new InvalidOperationException(
                    $"Ollama exited during startup with code {state.ExitCode}."
                    + (diagnostic is null ? string.Empty : $" {diagnostic}"));
            }

            if (supervisor.HasExited)
            {
                throw new InvalidOperationException(
                    $"The ollamactl supervisor exited during startup with code {supervisor.ExitCode}.");
            }

            await Task.Delay(TimeSpan.FromMilliseconds(50), cancellationToken).ConfigureAwait(false);
        }

        throw new TimeoutException("The Ollama process did not start within the supervisor timeout.");
    }

    private static string? TryReadDiagnostic(string path)
    {
        try
        {
            using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream);
            var contents = reader.ReadToEnd().Trim();
            const int maximumLength = 2048;
            return contents.Length <= maximumLength
                ? contents
                : contents[^maximumLength..];
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static async Task<FileStream> AcquireLockAsync(
        string lockFile,
        CancellationToken cancellationToken)
    {
        var deadline = DateTimeOffset.UtcNow.Add(LockTimeout);
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                return new FileStream(
                    lockFile,
                    FileMode.OpenOrCreate,
                    FileAccess.ReadWrite,
                    FileShare.None,
                    bufferSize: 1,
                    useAsync: true);
            }
            catch (IOException) when (DateTimeOffset.UtcNow < deadline)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(50), cancellationToken).ConfigureAwait(false);
            }
            catch (IOException exception)
            {
                throw new TimeoutException("Timed out waiting for the server operation lock.", exception);
            }
        }
    }

    private static bool IsRunningState(ServerProcessState state)
    {
        if (string.Equals(state.Status, ServerProcessState.ExitedStatus, StringComparison.Ordinal))
        {
            return false;
        }

        if (!IsOwnedProcess(
                state.SupervisorProcessId,
                state.SupervisorStartedAtUtc,
                state.SupervisorExecutable))
        {
            return false;
        }

        return state.TargetProcessId is null
            || state.TargetStartedAtUtc is null
            || IsOwnedProcess(state.TargetProcessId.Value, state.TargetStartedAtUtc.Value, state.TargetExecutable);
    }

    private static bool MayStillBeRunning(ServerProcessState state)
    {
        if (string.Equals(state.Status, ServerProcessState.ExitedStatus, StringComparison.Ordinal))
        {
            return false;
        }

        // Identity inspection can transiently fail immediately after process
        // creation (notably MainModule on Windows). Starting a second server is
        // riskier than conservatively reporting the live recorded supervisor;
        // StopAsync still requires full PID/start-time/executable validation.
        try
        {
            using var supervisor = Process.GetProcessById(state.SupervisorProcessId);
            return !supervisor.HasExited;
        }
        catch (Exception exception) when (exception is
            ArgumentException or
            InvalidOperationException or
            System.ComponentModel.Win32Exception or
            NotSupportedException)
        {
            return false;
        }
    }

    private static bool IsOwnedProcess(int processId, DateTimeOffset startedAtUtc, string executable)
    {
        if (!TryGetOwnedProcess(processId, startedAtUtc, executable, out var process))
        {
            return false;
        }

        process!.Dispose();
        return true;
    }

    private static bool TryGetOwnedProcess(
        int processId,
        DateTimeOffset startedAtUtc,
        string executable,
        out Process? process)
    {
        process = null;
        try
        {
            process = Process.GetProcessById(processId);
            var actualStartTime = process.StartTime.ToUniversalTime();
            var actualExecutable = TryGetExecutable(process);
            if (actualStartTime.Ticks == startedAtUtc.UtcDateTime.Ticks
                && !string.IsNullOrWhiteSpace(actualExecutable)
                && string.Equals(
                    Path.GetFullPath(actualExecutable),
                    Path.GetFullPath(executable),
                    OperatingSystem.IsWindows()
                        ? StringComparison.OrdinalIgnoreCase
                        : StringComparison.Ordinal))
            {
                return true;
            }

            process.Dispose();
            process = null;
            return false;
        }
        catch (Exception exception) when (exception is
            ArgumentException or
            InvalidOperationException or
            System.ComponentModel.Win32Exception or
            NotSupportedException or
            UnauthorizedAccessException)
        {
            process?.Dispose();
            process = null;
            return false;
        }
    }

    private static string? TryGetExecutable(Process process)
    {
        try
        {
            return process.MainModule?.FileName;
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

    private static async Task TerminateProcessTreeAsync(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (Exception exception) when (exception is
            InvalidOperationException or
            System.ComponentModel.Win32Exception or
            NotSupportedException)
        {
            return;
        }

        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is InvalidOperationException or OperationCanceledException)
        {
        }
    }

    private static ManagedServerStatus CreateStatus(
        ManagedServerState managedState,
        ServerProcessState? state,
        OllamactlPaths paths) => new(
        managedState,
        state?.SupervisorProcessId,
        state?.TargetProcessId,
        state?.TargetExecutable,
        state?.TargetStartedAtUtc ?? state?.SupervisorStartedAtUtc,
        state?.ExitedAtUtc,
        state?.ExitCode,
        paths.ServerStateFile,
        paths.StandardOutputLog,
        paths.StandardErrorLog);
}
