using Ollamactl.Application.Configuration;

namespace Ollamactl.Application.Abstractions;

public interface IOllamactlPathResolver
{
    OllamactlPaths Resolve(string? configDirectory, string? userHome = null);
}
