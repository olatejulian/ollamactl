using Ollamactl.Application.Configuration;

namespace Ollamactl.Application.Abstractions;

public interface IOllamactlConfigurationProvider
{
    Task<ResolvedExecutionContext> ResolveAsync(
        ConfigurationOverrides configurationOverrides,
        CancellationToken cancellationToken);
}
