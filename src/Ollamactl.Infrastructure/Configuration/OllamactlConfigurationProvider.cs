using System.Collections;
using System.Collections.ObjectModel;
using System.Globalization;
using Ollamactl.Application.Abstractions;
using Ollamactl.Application.Configuration;

namespace Ollamactl.Infrastructure.Configuration;

public sealed class OllamactlConfigurationProvider : IOllamactlConfigurationProvider
{
    private const string CommandLineOrigin = "command-line";
    private const string EnvironmentOrigin = "environment";
    private const string EnvFileOrigin = "env-file";
    private const string DefaultOrigin = "default";

    private readonly IOllamactlPathResolver pathResolver;
    private readonly Func<IReadOnlyDictionary<string, string>> readProcessEnvironment;

    public OllamactlConfigurationProvider()
        : this(new OllamactlPathResolver(), ReadCurrentProcessEnvironment)
    {
    }

    public OllamactlConfigurationProvider(IOllamactlPathResolver pathResolver)
        : this(pathResolver, ReadCurrentProcessEnvironment)
    {
    }

    public OllamactlConfigurationProvider(
        IOllamactlPathResolver pathResolver,
        Func<IReadOnlyDictionary<string, string>> readProcessEnvironment)
    {
        this.pathResolver = pathResolver ?? throw new ArgumentNullException(nameof(pathResolver));
        this.readProcessEnvironment = readProcessEnvironment
            ?? throw new ArgumentNullException(nameof(readProcessEnvironment));
    }

    public async Task<ResolvedExecutionContext> ResolveAsync(
        ConfigurationOverrides configurationOverrides,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(configurationOverrides);
        cancellationToken.ThrowIfCancellationRequested();

        var comparer = GetEnvironmentNameComparer();
        var processEnvironment = CopyEnvironment(readProcessEnvironment(), comparer);
        var processConfigDirectory = GetFirstNonEmpty(processEnvironment, "OLLAMACTL_CONFIG_DIR");
        var requestedConfigDirectory = FirstNonEmpty(
            configurationOverrides.ConfigDirectory,
            processConfigDirectory);
        var paths = pathResolver.Resolve(requestedConfigDirectory);

        var hasExplicitEnvFile = !string.IsNullOrWhiteSpace(configurationOverrides.EnvFile);
        var environmentFile = hasExplicitEnvFile
            ? Path.GetFullPath(configurationOverrides.EnvFile!)
            : paths.EnvFile;
        var fileEnvironment = await ReadEnvironmentFileAsync(
                environmentFile,
                hasExplicitEnvFile,
                cancellationToken)
            .ConfigureAwait(false);

        var effectiveEnvironment = new Dictionary<string, string>(comparer);
        var origins = new Dictionary<string, string>(comparer);
        MergeEnvironment(effectiveEnvironment, origins, fileEnvironment, EnvFileOrigin);
        MergeEnvironment(effectiveEnvironment, origins, processEnvironment, EnvironmentOrigin);

        var configuration = OllamaConfigurationResolver.Resolve(
            configurationOverrides.Host,
            configurationOverrides.TimeoutSeconds,
            paths,
            fileEnvironment,
            processEnvironment);

        var fileHost = GetFirstNonEmpty(fileEnvironment, "OLLAMA_HOST");
        var processHost = GetFirstNonEmpty(processEnvironment, "OLLAMA_HOST");
        var effectiveHost = FirstNonEmpty(
            configurationOverrides.Host,
            processHost,
            fileHost,
            OllamaEndpoint.DefaultHost)!;
        effectiveEnvironment["OLLAMA_HOST"] = effectiveHost.Trim();
        origins["OLLAMA_HOST"] = configuration.HostSource;

        effectiveEnvironment["OLLAMACTL_TIMEOUT_SECONDS"] = Convert.ToInt32(
                configuration.Timeout.TotalSeconds,
                CultureInfo.InvariantCulture)
            .ToString(CultureInfo.InvariantCulture);
        origins["OLLAMACTL_TIMEOUT_SECONDS"] = configuration.TimeoutSource;

        effectiveEnvironment["OLLAMACTL_CONFIG_DIR"] = paths.ConfigDirectory;
        origins["OLLAMACTL_CONFIG_DIR"] = !string.IsNullOrWhiteSpace(configurationOverrides.ConfigDirectory)
            ? CommandLineOrigin
            : !string.IsNullOrWhiteSpace(processConfigDirectory)
                ? EnvironmentOrigin
                : DefaultOrigin;

        return new ResolvedExecutionContext(
            configuration,
            environmentFile,
            new ReadOnlyDictionary<string, string>(effectiveEnvironment),
            new ReadOnlyDictionary<string, string>(origins));
    }

    private static async Task<IReadOnlyDictionary<string, string>> ReadEnvironmentFileAsync(
        string path,
        bool explicitPath,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
        {
            if (explicitPath)
            {
                throw new FileNotFoundException("The requested environment file was not found.", path);
            }

            return new Dictionary<string, string>(GetEnvironmentNameComparer());
        }

        var contents = await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
        return DotEnvParser.Parse(contents);
    }

    private static IReadOnlyDictionary<string, string> ReadCurrentProcessEnvironment()
    {
        var result = new Dictionary<string, string>(GetEnvironmentNameComparer());
        foreach (DictionaryEntry entry in Environment.GetEnvironmentVariables())
        {
            if (entry.Key is string name && entry.Value is string value)
            {
                result[name] = value;
            }
        }

        return result;
    }

    private static Dictionary<string, string> CopyEnvironment(
        IReadOnlyDictionary<string, string> source,
        IEqualityComparer<string> comparer)
    {
        ArgumentNullException.ThrowIfNull(source);
        var result = new Dictionary<string, string>(comparer);
        foreach (var variable in source)
        {
            if (string.IsNullOrWhiteSpace(variable.Key))
            {
                throw new ArgumentException("Environment variable names cannot be empty.", nameof(source));
            }

            result[variable.Key] = variable.Value;
        }

        return result;
    }

    private static void MergeEnvironment(
        Dictionary<string, string> effectiveEnvironment,
        Dictionary<string, string> origins,
        IReadOnlyDictionary<string, string> source,
        string origin)
    {
        foreach (var variable in source)
        {
            effectiveEnvironment[variable.Key] = variable.Value;
            origins[variable.Key] = origin;
        }
    }

    private static string? GetFirstNonEmpty(
        IReadOnlyDictionary<string, string> values,
        params string[] names)
    {
        foreach (var name in names)
        {
            if (values.TryGetValue(name, out var value) && !string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        return null;
    }

    private static string? FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));

    private static StringComparer GetEnvironmentNameComparer() =>
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
}
