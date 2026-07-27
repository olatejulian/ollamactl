namespace Ollamactl.Application.Processes;

public sealed record OllamaProcessInfo(
    int ProcessId,
    string Name,
    string? ExecutablePath,
    DateTimeOffset? StartedAtUtc,
    TimeSpan? CpuTime,
    long? WorkingSetBytes);
