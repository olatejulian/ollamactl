using Ollamactl.Application.Configuration;
using Ollamactl.Infrastructure.Configuration;

namespace Ollamactl.UnitTests;

[Trait("Category", "Unit")]
public sealed class OllamaConfigurationResolverTests
{
    [Theory]
    [InlineData("cli.example:1111", "env.example:2222", "file.example:3333", "http://cli.example:1111/", "command-line")]
    [InlineData(null, "env.example:2222", "file.example:3333", "http://env.example:2222/", "environment")]
    [InlineData(null, null, "file.example:3333", "http://file.example:3333/", "env-file")]
    [InlineData(null, null, null, "http://127.0.0.1:11434/", "default")]
    [InlineData(" ", " ", "file.example:3333", "http://file.example:3333/", "env-file")]
    public void ResolveAppliesHostPrecedence(
        string? commandLineHost,
        string? environmentHost,
        string? fileHost,
        string expectedEndpoint,
        string expectedSource)
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var fileValues = ValuesFrom(("OLLAMA_HOST", fileHost));
        var environmentValues = ValuesFrom(("OLLAMA_HOST", environmentHost));

        var result = OllamaConfigurationResolver.Resolve(
            commandLineHost,
            null,
            CreatePaths(temporaryDirectory.Path),
            fileValues,
            environmentValues);

        Assert.Equal(new Uri(expectedEndpoint), result.Endpoint);
        Assert.Equal(expectedSource, result.HostSource);
    }

    [Theory]
    [InlineData(41, "42", "43", 41, "command-line")]
    [InlineData(null, "42", "43", 42, "environment")]
    [InlineData(null, null, "43", 43, "env-file")]
    [InlineData(null, null, null, OllamaConfigurationResolver.DefaultTimeoutSeconds, "default")]
    [InlineData(null, " ", "43", 43, "env-file")]
    public void ResolveAppliesTimeoutPrecedence(
        int? commandLineTimeout,
        string? environmentTimeout,
        string? fileTimeout,
        int expectedSeconds,
        string expectedSource)
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var fileValues = ValuesFrom(("OLLAMACTL_TIMEOUT_SECONDS", fileTimeout));
        var environmentValues = ValuesFrom(("OLLAMACTL_TIMEOUT_SECONDS", environmentTimeout));

        var result = OllamaConfigurationResolver.Resolve(
            null,
            commandLineTimeout,
            CreatePaths(temporaryDirectory.Path),
            fileValues,
            environmentValues);

        Assert.Equal(TimeSpan.FromSeconds(expectedSeconds), result.Timeout);
        Assert.Equal(expectedSource, result.TimeoutSource);
    }

    [Fact]
    public void ResolveUsesStartupTimeoutAliasesInPriorityOrder()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var paths = CreatePaths(temporaryDirectory.Path);
        var fileValues = ValuesFrom(
            ("OLLAMACTL_TIMEOUT_SECONDS", "53"),
            ("OLLAMA_STARTUP_TIMEOUT", "54"));
        var environmentValues = ValuesFrom(
            ("OLLAMACTL_TIMEOUT_SECONDS", null),
            ("OLLAMA_STARTUP_TIMEOUT", "52"));

        var environmentResult = OllamaConfigurationResolver.Resolve(
            null,
            null,
            paths,
            fileValues,
            environmentValues);
        var fileResult = OllamaConfigurationResolver.Resolve(
            null,
            null,
            paths,
            fileValues,
            ValuesFrom());

        Assert.Equal(TimeSpan.FromSeconds(52), environmentResult.Timeout);
        Assert.Equal("environment", environmentResult.TimeoutSource);
        Assert.Equal(TimeSpan.FromSeconds(53), fileResult.Timeout);
        Assert.Equal("env-file", fileResult.TimeoutSource);
    }

    [Fact]
    public void PathResolverDerivesExplicitAndDefaultPaths()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var explicitDirectory = System.IO.Path.Combine(temporaryDirectory.Path, "explicit");
        var homeDirectory = System.IO.Path.Combine(temporaryDirectory.Path, "home");
        var resolver = new OllamactlPathResolver();

        var explicitPaths = resolver.Resolve(explicitDirectory, homeDirectory);
        var defaultPaths = resolver.Resolve(null, homeDirectory);

        AssertPaths(explicitPaths, explicitDirectory);
        AssertPaths(defaultPaths, System.IO.Path.Combine(homeDirectory, ".config", "ollama"));
    }

    [Theory]
    [InlineData(0, null, null, "between 1 and 600")]
    [InlineData(601, null, null, "between 1 and 600")]
    [InlineData(null, "not-a-number", null, "Invalid timeout value in environment")]
    [InlineData(null, null, "-1", "Invalid timeout value in env file")]
    [InlineData(null, null, "601", "between 1 and 600")]
    public void ResolveRejectsInvalidEffectiveTimeout(
        int? commandLineTimeout,
        string? environmentTimeout,
        string? fileTimeout,
        string expectedMessage)
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var fileValues = ValuesFrom(("OLLAMACTL_TIMEOUT_SECONDS", fileTimeout));
        var environmentValues = ValuesFrom(("OLLAMACTL_TIMEOUT_SECONDS", environmentTimeout));

        var exception = Assert.Throws<FormatException>(() => OllamaConfigurationResolver.Resolve(
            null,
            commandLineTimeout,
            CreatePaths(temporaryDirectory.Path),
            fileValues,
            environmentValues));

        Assert.Contains(expectedMessage, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ResolveDoesNotParseInvalidLowerPriorityTimeouts()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var fileValues = ValuesFrom(("OLLAMACTL_TIMEOUT_SECONDS", "also-invalid"));
        var environmentValues = ValuesFrom(("OLLAMACTL_TIMEOUT_SECONDS", "invalid"));

        var result = OllamaConfigurationResolver.Resolve(
            null,
            45,
            CreatePaths(temporaryDirectory.Path),
            fileValues,
            environmentValues);

        Assert.Equal(TimeSpan.FromSeconds(45), result.Timeout);
        Assert.Equal("command-line", result.TimeoutSource);
    }

    private static Dictionary<string, string> ValuesFrom(
        params (string Name, string? Value)[] entries) =>
        entries
            .Where(entry => entry.Value is not null)
            .ToDictionary(entry => entry.Name, entry => entry.Value!, StringComparer.OrdinalIgnoreCase);

    private static OllamactlPaths CreatePaths(string configDirectory) =>
        new OllamactlPathResolver().Resolve(configDirectory);

    private static void AssertPaths(OllamactlPaths paths, string expectedConfigDirectory)
    {
        var fullConfigDirectory = System.IO.Path.GetFullPath(expectedConfigDirectory);
        Assert.Equal(fullConfigDirectory, paths.ConfigDirectory);
        Assert.Equal(System.IO.Path.Combine(fullConfigDirectory, ".env"), paths.EnvFile);
        Assert.Equal(System.IO.Path.Combine(fullConfigDirectory, "logs"), paths.LogDirectory);
        Assert.Equal(System.IO.Path.Combine(fullConfigDirectory, "run"), paths.RunDirectory);
        Assert.Equal(System.IO.Path.Combine(fullConfigDirectory, "run", "ollamactl-server.json"), paths.ServerStateFile);
    }
}
