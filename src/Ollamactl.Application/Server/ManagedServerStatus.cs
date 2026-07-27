namespace Ollamactl.Application.Server;

public sealed record ManagedServerStatus(
    ManagedServerState State,
    int? SupervisorProcessId,
    int? ProcessId,
    string? Executable,
    DateTimeOffset? StartedAtUtc,
    DateTimeOffset? ExitedAtUtc,
    int? ExitCode,
    string StateFile,
    string StandardOutputLog,
    string StandardErrorLog);
