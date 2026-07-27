namespace Ollamactl.Application.Server;

public sealed record ServerStopResult(
    bool Stopped,
    int? ProcessId,
    string Message);
