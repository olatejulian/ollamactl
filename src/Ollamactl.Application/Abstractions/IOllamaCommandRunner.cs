using Ollamactl.Application.Execution;

namespace Ollamactl.Application.Abstractions;

public interface IOllamaCommandRunner
{
    Task<OllamaCommandResult> RunAsync(
        OllamaCommandRequest request,
        CancellationToken cancellationToken);
}
