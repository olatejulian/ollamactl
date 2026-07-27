namespace Ollamactl.Application.Logging;

public sealed record OllamaLogTail(
    OllamaLogSource Source,
    string Path,
    IReadOnlyList<OllamaLogLine> Lines);
