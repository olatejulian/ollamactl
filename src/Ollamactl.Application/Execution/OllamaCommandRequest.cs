namespace Ollamactl.Application.Execution;

public sealed record OllamaCommandRequest(
    string Executable,
    IReadOnlyList<string> Arguments,
    IReadOnlyDictionary<string, string> Environment,
    OllamaCommandMode Mode = OllamaCommandMode.Capture,
    string? WorkingDirectory = null,
    TimeSpan? Timeout = null);
