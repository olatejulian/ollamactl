using Ollamactl.Application.Processes;

namespace Ollamactl.Application.Abstractions;

public interface IOllamaProcessInspector
{
    Task<IReadOnlyList<OllamaProcessInfo>> InspectAsync(CancellationToken cancellationToken);
}
