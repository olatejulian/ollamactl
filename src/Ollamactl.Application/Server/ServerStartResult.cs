namespace Ollamactl.Application.Server;

public sealed record ServerStartResult(
    bool Started,
    int SupervisorProcessId,
    int? ProcessId,
    string Executable,
    string StateFile,
    string StandardOutputLog,
    string StandardErrorLog);
