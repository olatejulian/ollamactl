namespace Ollamactl.Application.Abstractions;

public interface IOllamaExecutableResolver
{
    string Resolve(
        string? commandLineExecutable,
        IReadOnlyDictionary<string, string> effectiveEnvironment);
}
