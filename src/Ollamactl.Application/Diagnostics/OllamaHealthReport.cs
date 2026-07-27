using Ollamactl.Application.Logging;
using Ollamactl.Application.Processes;
using Ollamactl.Application.Server;
using Ollamactl.Domain.Models;

namespace Ollamactl.Application.Diagnostics;

public sealed record OllamaHealthReport(
    DateTimeOffset CheckedAtUtc,
    OllamaHealthStatus Status,
    Uri Endpoint,
    string? ApiVersion,
    IReadOnlyList<OllamaModel> Models,
    IReadOnlyList<RunningOllamaModel> RunningModels,
    ManagedServerStatus? ManagedServer,
    IReadOnlyList<OllamaProcessInfo> Processes,
    IReadOnlyList<OllamaLogTail> Logs,
    IReadOnlyList<DiagnosticCheck> Checks,
    long DurationMilliseconds);
