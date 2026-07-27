namespace Ollamactl.Application.Logging;

public sealed record OllamaLogLine(
    long LineNumber,
    string Text,
    bool IsSuspicious,
    bool WasRedacted);
