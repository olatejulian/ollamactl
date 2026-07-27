namespace Ollamactl.Application.Diagnostics;

public sealed record DiagnosticCheck(
    string Id,
    DiagnosticCheckStatus Status,
    string Summary,
    string? Details,
    string? SuggestedAction,
    long DurationMilliseconds);
