namespace Ollamactl.Application.Execution;

public sealed record OllamaCommandResult(
    int ExitCode,
    string? StandardOutput,
    string? StandardError);
