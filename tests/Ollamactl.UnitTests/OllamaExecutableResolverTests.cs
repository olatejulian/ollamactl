using Ollamactl.Infrastructure.Processes;

namespace Ollamactl.UnitTests;

[Trait("Category", "Unit")]
public sealed class OllamaExecutableResolverTests
{
    [Fact]
    public void ResolveUsesCommandLineBeforeEnvironmentAndPath()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var commandLineExecutable = CreateFile(temporaryDirectory.Path, "command-ollama.exe");
        var environmentExecutable = CreateFile(temporaryDirectory.Path, "environment-ollama.exe");
        var pathDirectory = System.IO.Path.Combine(temporaryDirectory.Path, "path");
        Directory.CreateDirectory(pathDirectory);
        CreateFile(pathDirectory, GetPathExecutableName());
        var environment = EnvironmentFrom(
            ("OLLAMA_EXE", environmentExecutable),
            ("PATH", pathDirectory));
        var resolver = new OllamaExecutableResolver(currentExecutablePath: null);

        var result = resolver.Resolve(commandLineExecutable, environment);

        Assert.Equal(System.IO.Path.GetFullPath(commandLineExecutable), result);
    }

    [Fact]
    public void ResolveUsesEffectiveOllamaExeBeforePath()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var environmentExecutable = CreateFile(temporaryDirectory.Path, "environment-ollama.exe");
        var pathDirectory = System.IO.Path.Combine(temporaryDirectory.Path, "path");
        Directory.CreateDirectory(pathDirectory);
        CreateFile(pathDirectory, GetPathExecutableName());
        var environment = EnvironmentFrom(
            ("OLLAMA_EXE", environmentExecutable),
            ("PATH", pathDirectory));
        var resolver = new OllamaExecutableResolver(currentExecutablePath: null);

        var result = resolver.Resolve(null, environment);

        Assert.Equal(System.IO.Path.GetFullPath(environmentExecutable), result);
    }

    [Fact]
    public void ResolveFindsOllamaOnEffectivePath()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var pathDirectory = System.IO.Path.Combine(temporaryDirectory.Path, "path");
        Directory.CreateDirectory(pathDirectory);
        var pathExecutable = CreateFile(pathDirectory, GetPathExecutableName());
        var resolver = new OllamaExecutableResolver(currentExecutablePath: null);

        var result = resolver.Resolve(null, EnvironmentFrom(("PATH", pathDirectory)));

        Assert.Equal(System.IO.Path.GetFullPath(pathExecutable), result);
    }

    [Fact]
    public void ResolveRejectsRecursionToCurrentOllamactlExecutable()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var currentExecutable = CreateFile(temporaryDirectory.Path, "ollamactl.exe");
        var resolver = new OllamaExecutableResolver(currentExecutable);

        var exception = Assert.Throws<InvalidOperationException>(() =>
            resolver.Resolve(currentExecutable, EnvironmentFrom()));

        Assert.Contains("recursively", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ResolveDoesNotFallBackWhenExplicitExecutableIsMissing()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var missingExecutable = System.IO.Path.Combine(temporaryDirectory.Path, "missing.exe");
        var pathExecutable = CreateFile(temporaryDirectory.Path, GetPathExecutableName());
        var resolver = new OllamaExecutableResolver(currentExecutablePath: null);

        var exception = Assert.Throws<FileNotFoundException>(() =>
            resolver.Resolve(
                missingExecutable,
                EnvironmentFrom(("PATH", System.IO.Path.GetDirectoryName(pathExecutable)!))));

        Assert.Equal(System.IO.Path.GetFullPath(missingExecutable), exception.FileName);
    }

    private static string GetPathExecutableName() =>
        OperatingSystem.IsWindows() ? "ollama.cmd" : "ollama";

    private static string CreateFile(string directory, string name)
    {
        var path = System.IO.Path.Combine(directory, name);
        File.WriteAllText(path, string.Empty);
        return path;
    }

    private static Dictionary<string, string> EnvironmentFrom(
        params (string Name, string Value)[] variables) =>
        variables.ToDictionary(
            variable => variable.Name,
            variable => variable.Value,
            StringComparer.OrdinalIgnoreCase);
}
