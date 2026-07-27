using System.Globalization;
using Ollamactl.Application.Abstractions;
using Ollamactl.Application.Configuration;
using Ollamactl.Application.Execution;
using Ollamactl.Application.Logging;
using Ollamactl.Application.Processes;
using Ollamactl.Application.Server;
using Ollamactl.Cli.Runtime;
using Ollamactl.Domain.Models;

namespace Ollamactl.UnitTests;

internal sealed class TemporaryDirectory : IDisposable
{
    public TemporaryDirectory()
    {
        Path = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            "ollamactl-unit-tests",
            Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture));
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public void Dispose()
    {
        if (Directory.Exists(Path))
        {
            Directory.Delete(Path, recursive: true);
        }

        GC.SuppressFinalize(this);
    }
}

internal sealed class TestCliConsole : ICliConsole, IDisposable
{
    private readonly StringWriter standardOutput = new(CultureInfo.InvariantCulture);
    private readonly StringWriter standardError = new(CultureInfo.InvariantCulture);

    public TextWriter StandardOutput => standardOutput;

    public TextWriter StandardError => standardError;

    public string Output => standardOutput.ToString();

    public string Error => standardError.ToString();

    public void Dispose()
    {
        standardOutput.Dispose();
        standardError.Dispose();
        GC.SuppressFinalize(this);
    }
}

internal sealed class RecordingOllamaApiClientFactory(RecordingOllamaApiClient client) : IOllamaApiClientFactory
{
    public int CreateCount { get; private set; }

    public ResolvedOllamaConfiguration? Configuration { get; private set; }

    public IOllamaApiClient Create(ResolvedOllamaConfiguration configuration)
    {
        CreateCount++;
        Configuration = configuration;
        return client;
    }
}

internal sealed class RecordingOllamaApiClient : IOllamaApiClient
{
    public Uri Endpoint { get; init; } = new("http://127.0.0.1:11434/");

    public string Version { get; init; } = "0.9.0";

    public Exception? VersionException { get; init; }

    public Queue<object> VersionResponses { get; } = new();

    public OllamaModel[] Models { get; init; } = [];

    public RunningOllamaModel[] RunningModels { get; init; } = [];

    public (string Model, string KeepAlive)? LoadedModel { get; private set; }

    public string? UnloadedModel { get; private set; }

    public bool IsDisposed { get; private set; }

    public Task<string> GetVersionAsync(CancellationToken cancellationToken)
    {
        var response = VersionResponses.Count > 0 ? VersionResponses.Dequeue() : null;
        return response switch
        {
            string version => Task.FromResult(version),
            Exception exception => Task.FromException<string>(exception),
            _ when VersionException is not null => Task.FromException<string>(VersionException),
            _ => Task.FromResult(Version),
        };
    }

    public Task<OllamaModel[]> GetModelsAsync(CancellationToken cancellationToken) => Task.FromResult(Models);

    public Task<RunningOllamaModel[]> GetRunningModelsAsync(CancellationToken cancellationToken) =>
        Task.FromResult(RunningModels);

    public Task LoadModelAsync(string model, string keepAlive, CancellationToken cancellationToken)
    {
        LoadedModel = (model, keepAlive);
        return Task.CompletedTask;
    }

    public Task UnloadModelAsync(string model, CancellationToken cancellationToken)
    {
        UnloadedModel = model;
        return Task.CompletedTask;
    }

    public Task<ChatResult> ChatAsync(string model, string prompt, CancellationToken cancellationToken) =>
        Task.FromResult(new ChatResult(model, prompt));

    public Task<ToolProbeResult> ProbeToolsAsync(string model, CancellationToken cancellationToken) =>
        Task.FromResult(new ToolProbeResult(model, true, "test", "{}", 1));

    public void Dispose()
    {
        IsDisposed = true;
        GC.SuppressFinalize(this);
    }
}

internal sealed class RecordingOllamaServerManager : IOllamaServerManager
{
    public ServerStartOptions? StartOptions { get; private set; }

    public ServerStartResult StartResult { get; init; } = new(
        false,
        42,
        null,
        "ollama.exe",
        "state.json",
        "stdout.log",
        "stderr.log");

    public string? StoppedConfigDirectory { get; private set; }

    public ServerStopResult StopResult { get; init; } = new(false, null, "No managed server is running.");

    public string? StatusConfigDirectory { get; private set; }

    public ManagedServerStatus StatusResult { get; init; } = new(
        ManagedServerState.NotManaged,
        null,
        null,
        null,
        null,
        null,
        null,
        "state.json",
        "stdout.log",
        "stderr.log");

    public Task<ServerStartResult> StartAsync(ServerStartOptions options, CancellationToken cancellationToken)
    {
        StartOptions = options;
        return Task.FromResult(StartResult);
    }

    public Task<ServerStopResult> StopAsync(string configDirectory, CancellationToken cancellationToken)
    {
        StoppedConfigDirectory = configDirectory;
        return Task.FromResult(StopResult);
    }

    public Task<ManagedServerStatus> GetStatusAsync(
        string configDirectory,
        CancellationToken cancellationToken)
    {
        StatusConfigDirectory = configDirectory;
        return Task.FromResult(StatusResult);
    }
}

internal sealed class RecordingOllamaExecutableResolver : IOllamaExecutableResolver
{
    public string Executable { get; init; } = "resolved-ollama.exe";

    public Exception? Exception { get; init; }

    public string? CommandLineExecutable { get; private set; }

    public IReadOnlyDictionary<string, string>? EffectiveEnvironment { get; private set; }

    public int ResolveCount { get; private set; }

    public string Resolve(
        string? commandLineExecutable,
        IReadOnlyDictionary<string, string> effectiveEnvironment)
    {
        ResolveCount++;
        CommandLineExecutable = commandLineExecutable;
        EffectiveEnvironment = effectiveEnvironment;
        return Exception is null ? Executable : throw Exception;
    }
}

internal sealed class RecordingOllamaCommandRunner : IOllamaCommandRunner
{
    public OllamaCommandResult Result { get; init; } = new(0, "ollama version 0.9.0", string.Empty);

    public Exception? Exception { get; init; }

    public OllamaCommandRequest? Request { get; private set; }

    public int RunCount { get; private set; }

    public Task<OllamaCommandResult> RunAsync(
        OllamaCommandRequest request,
        CancellationToken cancellationToken)
    {
        RunCount++;
        Request = request;
        return Exception is null
            ? Task.FromResult(Result)
            : Task.FromException<OllamaCommandResult>(Exception);
    }
}

internal sealed class RecordingOllamaProcessInspector : IOllamaProcessInspector
{
    public IReadOnlyList<OllamaProcessInfo> Processes { get; init; } = [];

    public int InspectCount { get; private set; }

    public Task<IReadOnlyList<OllamaProcessInfo>> InspectAsync(CancellationToken cancellationToken)
    {
        InspectCount++;
        return Task.FromResult(Processes);
    }
}

internal sealed class RecordingOllamaLogReader : IOllamaLogReader
{
    public IReadOnlyList<OllamaLogTail> Logs { get; init; } = [];

    public string? ConfigDirectory { get; private set; }

    public int? MaximumLines { get; private set; }

    public Task<IReadOnlyList<OllamaLogTail>> ReadTailAsync(
        string? configDirectory,
        int maximumLines,
        CancellationToken cancellationToken)
    {
        ConfigDirectory = configDirectory;
        MaximumLines = maximumLines;
        return Task.FromResult(Logs);
    }
}
