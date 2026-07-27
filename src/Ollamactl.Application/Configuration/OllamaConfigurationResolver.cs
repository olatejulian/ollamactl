namespace Ollamactl.Application.Configuration;

public static class OllamaConfigurationResolver
{
    public const int DefaultTimeoutSeconds = 30;
    public const int MaximumTimeoutSeconds = 600;

    public static ResolvedOllamaConfiguration Resolve(
        string? commandLineHost,
        int? commandLineTimeoutSeconds,
        OllamactlPaths paths,
        IReadOnlyDictionary<string, string>? fileValues = null,
        IReadOnlyDictionary<string, string>? environmentValues = null)
    {
        ArgumentNullException.ThrowIfNull(paths);
        fileValues ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        environmentValues ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        var environmentHost = GetFirst(environmentValues, "OLLAMA_HOST");
        fileValues.TryGetValue("OLLAMA_HOST", out var fileHost);
        var host = FirstNonEmpty(commandLineHost, environmentHost, fileHost, OllamaEndpoint.DefaultHost);
        var hostSource = !string.IsNullOrWhiteSpace(commandLineHost)
            ? "command-line"
            : !string.IsNullOrWhiteSpace(environmentHost)
                ? "environment"
                : !string.IsNullOrWhiteSpace(fileHost)
                    ? "env-file"
                    : "default";

        var environmentTimeout = GetFirst(
            environmentValues,
            "OLLAMACTL_TIMEOUT_SECONDS",
            "OLLAMA_STARTUP_TIMEOUT");
        var fileTimeout = GetFirst(fileValues, "OLLAMACTL_TIMEOUT_SECONDS", "OLLAMA_STARTUP_TIMEOUT");

        var timeoutSource = commandLineTimeoutSeconds.HasValue
            ? "command-line"
            : !string.IsNullOrWhiteSpace(environmentTimeout)
                ? "environment"
                : !string.IsNullOrWhiteSpace(fileTimeout)
                    ? "env-file"
                    : "default";

        var timeoutSeconds = commandLineTimeoutSeconds
            ?? ParseTimeout(environmentTimeout, "environment")
            ?? ParseTimeout(fileTimeout, "env file")
            ?? DefaultTimeoutSeconds;

        if (timeoutSeconds is < 1 or > MaximumTimeoutSeconds)
        {
            throw new FormatException($"Timeout must be between 1 and {MaximumTimeoutSeconds} seconds.");
        }

        return new ResolvedOllamaConfiguration(
            OllamaEndpoint.Parse(host),
            TimeSpan.FromSeconds(timeoutSeconds),
            paths,
            hostSource,
            timeoutSource);
    }

    private static int? ParseTimeout(string? value, string source)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (!int.TryParse(
                value,
                System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture,
                out var result))
        {
            throw new FormatException($"Invalid timeout value in {source}.");
        }

        return result;
    }

    private static string? GetFirst(IReadOnlyDictionary<string, string> values, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (values.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        return null;
    }

    private static string? FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
}
