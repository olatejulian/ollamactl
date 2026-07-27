using Ollamactl.Application.Configuration;
using Ollamactl.Infrastructure.Configuration;

namespace Ollamactl.UnitTests;

[Trait("Category", "Unit")]
public sealed class OllamactlConfigurationProviderTests
{
    [Fact]
    public async Task ResolveAppliesCommandEnvironmentFileAndDefaultPrecedence()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var configDirectory = System.IO.Path.Combine(temporaryDirectory.Path, "config");
        var environmentFile = System.IO.Path.Combine(temporaryDirectory.Path, "selected.env");
        Directory.CreateDirectory(configDirectory);
        await File.WriteAllTextAsync(
            environmentFile,
            string.Join(
                '\n',
                "OLLAMA_HOST=file.example:3333",
                "OLLAMACTL_TIMEOUT_SECONDS=43",
                "OLLAMA_EXE=file-ollama.exe",
                "SHARED_VALUE=from-file",
                "FILE_ONLY=from-file",
                "SECRET_TOKEN=do-not-copy-into-origins"));

        var processEnvironment = EnvironmentFrom(
            ("OLLAMA_HOST", "environment.example:2222"),
            ("OLLAMACTL_TIMEOUT_SECONDS", "42"),
            ("OLLAMA_EXE", "environment-ollama.exe"),
            ("SHARED_VALUE", "from-environment"));
        var provider = new OllamactlConfigurationProvider(
            new OllamactlPathResolver(),
            () => processEnvironment);

        var context = await provider.ResolveAsync(
            new ConfigurationOverrides(
                Host: "http://command.example:1111",
                TimeoutSeconds: 41,
                ConfigDirectory: configDirectory,
                EnvFile: environmentFile),
            CancellationToken.None);

        Assert.Equal(new Uri("http://command.example:1111/"), context.Configuration.Endpoint);
        Assert.Equal(TimeSpan.FromSeconds(41), context.Configuration.Timeout);
        Assert.Equal("command-line", context.Configuration.HostSource);
        Assert.Equal("command-line", context.Configuration.TimeoutSource);
        Assert.Equal(System.IO.Path.GetFullPath(environmentFile), context.EnvironmentFile);
        Assert.Equal("http://command.example:1111", context.EffectiveEnvironment["OLLAMA_HOST"]);
        Assert.Equal("41", context.EffectiveEnvironment["OLLAMACTL_TIMEOUT_SECONDS"]);
        Assert.Equal("environment-ollama.exe", context.EffectiveEnvironment["OLLAMA_EXE"]);
        Assert.Equal("from-environment", context.EffectiveEnvironment["SHARED_VALUE"]);
        Assert.Equal("from-file", context.EffectiveEnvironment["FILE_ONLY"]);
        Assert.Equal("command-line", context.Origins["OLLAMA_HOST"]);
        Assert.Equal("environment", context.Origins["OLLAMA_EXE"]);
        Assert.Equal("environment", context.Origins["SHARED_VALUE"]);
        Assert.Equal("env-file", context.Origins["FILE_ONLY"]);
        Assert.Equal("env-file", context.Origins["SECRET_TOKEN"]);
        Assert.DoesNotContain(
            context.Origins.Values,
            value => value.Contains("do-not-copy", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ResolveReadsDefaultEnvironmentFileAndMapsHostForChildProcesses()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var configDirectory = System.IO.Path.Combine(temporaryDirectory.Path, "config");
        Directory.CreateDirectory(configDirectory);
        var defaultEnvironmentFile = System.IO.Path.Combine(configDirectory, ".env");
        await File.WriteAllTextAsync(
            defaultEnvironmentFile,
            "OLLAMA_HOST=from-default-file.example:3333\nFILE_SETTING=enabled\n");
        var provider = new OllamactlConfigurationProvider(
            new OllamactlPathResolver(),
            static () => EnvironmentFrom());

        var context = await provider.ResolveAsync(
            new ConfigurationOverrides(ConfigDirectory: configDirectory),
            CancellationToken.None);

        Assert.Equal(System.IO.Path.GetFullPath(defaultEnvironmentFile), context.EnvironmentFile);
        Assert.Equal(
            new Uri("http://from-default-file.example:3333/"),
            context.Configuration.Endpoint);
        Assert.Equal(
            "from-default-file.example:3333",
            context.EffectiveEnvironment["OLLAMA_HOST"]);
        Assert.Equal("env-file", context.Origins["OLLAMA_HOST"]);
        Assert.Equal("enabled", context.EffectiveEnvironment["FILE_SETTING"]);
    }

    [Fact]
    public async Task ResolveUsesDefaultsWhenDefaultEnvironmentFileIsMissing()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var configDirectory = System.IO.Path.Combine(temporaryDirectory.Path, "config");
        var provider = new OllamactlConfigurationProvider(
            new OllamactlPathResolver(),
            static () => EnvironmentFrom());

        var context = await provider.ResolveAsync(
            new ConfigurationOverrides(ConfigDirectory: configDirectory),
            CancellationToken.None);

        Assert.Equal(
            new Uri("http://127.0.0.1:11434/"),
            context.Configuration.Endpoint);
        Assert.Equal("127.0.0.1:11434", context.EffectiveEnvironment["OLLAMA_HOST"]);
        Assert.Equal("30", context.EffectiveEnvironment["OLLAMACTL_TIMEOUT_SECONDS"]);
        Assert.Equal("default", context.Origins["OLLAMA_HOST"]);
        Assert.Equal("default", context.Origins["OLLAMACTL_TIMEOUT_SECONDS"]);
    }

    [Fact]
    public async Task ResolveRejectsMissingExplicitEnvironmentFile()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var missingFile = System.IO.Path.Combine(temporaryDirectory.Path, "missing.env");
        var provider = new OllamactlConfigurationProvider(
            new OllamactlPathResolver(),
            static () => EnvironmentFrom());

        var exception = await Assert.ThrowsAsync<FileNotFoundException>(() =>
            provider.ResolveAsync(
                new ConfigurationOverrides(
                    ConfigDirectory: temporaryDirectory.Path,
                    EnvFile: missingFile),
                CancellationToken.None));

        Assert.Equal(System.IO.Path.GetFullPath(missingFile), exception.FileName);
    }

    private static Dictionary<string, string> EnvironmentFrom(
        params (string Name, string Value)[] variables) =>
        variables.ToDictionary(
            variable => variable.Name,
            variable => variable.Value,
            StringComparer.OrdinalIgnoreCase);
}
