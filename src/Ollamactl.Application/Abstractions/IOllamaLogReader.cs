using Ollamactl.Application.Logging;

namespace Ollamactl.Application.Abstractions;

public interface IOllamaLogReader
{
    Task<IReadOnlyList<OllamaLogTail>> ReadTailAsync(
        string? configDirectory,
        int maximumLines,
        CancellationToken cancellationToken);
}
