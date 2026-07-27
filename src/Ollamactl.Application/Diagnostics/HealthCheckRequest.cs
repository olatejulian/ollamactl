using Ollamactl.Application.Configuration;

namespace Ollamactl.Application.Diagnostics;

public sealed record HealthCheckRequest(
    ResolvedExecutionContext ExecutionContext,
    string? OllamaExecutable,
    string? ExecutableResolutionError = null,
    bool IncludeLogs = false,
    int LogTail = 100);
