using Ollamactl.Application.Configuration;

namespace Ollamactl.Application.Abstractions;

public interface IOllamaApiClientFactory
{
    IOllamaApiClient Create(ResolvedOllamaConfiguration configuration);
}
