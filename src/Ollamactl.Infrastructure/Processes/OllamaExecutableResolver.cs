using Ollamactl.Application.Abstractions;

namespace Ollamactl.Infrastructure.Processes;

public sealed class OllamaExecutableResolver : IOllamaExecutableResolver
{
    private readonly string? currentExecutablePath;

    public OllamaExecutableResolver()
        : this(Environment.ProcessPath)
    {
    }

    public OllamaExecutableResolver(string? currentExecutablePath)
    {
        this.currentExecutablePath = string.IsNullOrWhiteSpace(currentExecutablePath)
            ? null
            : GetCanonicalPath(currentExecutablePath);
    }

    public string Resolve(
        string? commandLineExecutable,
        IReadOnlyDictionary<string, string> effectiveEnvironment)
    {
        ArgumentNullException.ThrowIfNull(effectiveEnvironment);

        if (!string.IsNullOrWhiteSpace(commandLineExecutable))
        {
            return ValidateCandidate(commandLineExecutable, "command-line option");
        }

        var environmentExecutable = GetEnvironmentVariable(effectiveEnvironment, "OLLAMA_EXE");
        if (!string.IsNullOrWhiteSpace(environmentExecutable))
        {
            return ValidateCandidate(environmentExecutable, "OLLAMA_EXE");
        }

        var pathValue = GetEnvironmentVariable(effectiveEnvironment, "PATH") ?? string.Empty;
        foreach (var directory in pathValue.Split(
                     Path.PathSeparator,
                     StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            foreach (var executableName in GetExecutableNames())
            {
                var candidate = Path.Combine(directory.Trim('"'), executableName);
                if (File.Exists(candidate))
                {
                    return ValidateCandidate(candidate, "PATH");
                }
            }
        }

        throw new FileNotFoundException(
            "Could not find the Ollama executable. Use --ollama-path, set OLLAMA_EXE, or add Ollama to PATH.");
    }

    private string ValidateCandidate(string candidate, string source)
    {
        var fullPath = GetCanonicalPath(candidate.Trim());
        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException(
                $"The Ollama executable selected from {source} was not found.",
                fullPath);
        }

        if (currentExecutablePath is not null && PathsEqual(fullPath, currentExecutablePath))
        {
            throw new InvalidOperationException(
                "Refusing to launch ollamactl recursively as the Ollama executable.");
        }

        return fullPath;
    }

    private static string? GetEnvironmentVariable(
        IReadOnlyDictionary<string, string> environment,
        string name)
    {
        if (environment.TryGetValue(name, out var value))
        {
            return value;
        }

        if (!OperatingSystem.IsWindows())
        {
            return null;
        }

        foreach (var variable in environment)
        {
            if (string.Equals(variable.Key, name, StringComparison.OrdinalIgnoreCase))
            {
                return variable.Value;
            }
        }

        return null;
    }

    private static IEnumerable<string> GetExecutableNames() =>
        OperatingSystem.IsWindows()
            ? ["ollama.exe", "ollama.cmd", "ollama.bat"]
            : ["ollama"];

    private static string GetCanonicalPath(string path)
    {
        var fullPath = Path.GetFullPath(path);
        try
        {
            var file = new FileInfo(fullPath);
            return file.LinkTarget is null
                ? fullPath
                : file.ResolveLinkTarget(returnFinalTarget: true)?.FullName ?? fullPath;
        }
        catch (IOException)
        {
            return fullPath;
        }
        catch (UnauthorizedAccessException)
        {
            return fullPath;
        }
    }

    private static bool PathsEqual(string left, string right) =>
        string.Equals(
            Path.GetFullPath(left),
            Path.GetFullPath(right),
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
}
