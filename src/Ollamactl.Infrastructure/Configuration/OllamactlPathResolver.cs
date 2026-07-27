using Ollamactl.Application.Abstractions;
using Ollamactl.Application.Configuration;

namespace Ollamactl.Infrastructure.Configuration;

public sealed class OllamactlPathResolver : IOllamactlPathResolver
{
    public OllamactlPaths Resolve(string? configDirectory, string? userHome = null)
    {
        var resolvedConfigDirectory = configDirectory;
        if (string.IsNullOrWhiteSpace(resolvedConfigDirectory))
        {
            var home = string.IsNullOrWhiteSpace(userHome)
                ? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
                : userHome;

            if (string.IsNullOrWhiteSpace(home))
            {
                throw new InvalidOperationException("Unable to determine the user profile directory.");
            }

            resolvedConfigDirectory = Path.Combine(home, ".config", "ollama");
        }

        resolvedConfigDirectory = Path.GetFullPath(resolvedConfigDirectory);
        var logDirectory = Path.Combine(resolvedConfigDirectory, "logs");
        var runDirectory = Path.Combine(resolvedConfigDirectory, "run");

        return new OllamactlPaths(
            resolvedConfigDirectory,
            Path.Combine(resolvedConfigDirectory, ".env"),
            logDirectory,
            runDirectory,
            Path.Combine(runDirectory, "ollamactl-server.json"),
            Path.Combine(logDirectory, "ollama.out.log"),
            Path.Combine(logDirectory, "ollama.err.log"));
    }
}
