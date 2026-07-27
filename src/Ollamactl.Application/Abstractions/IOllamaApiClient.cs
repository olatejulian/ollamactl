using Ollamactl.Domain.Models;

namespace Ollamactl.Application.Abstractions;

public interface IOllamaApiClient : IDisposable
{
    Uri Endpoint { get; }

    Task<string> GetVersionAsync(CancellationToken cancellationToken);

    Task<OllamaModel[]> GetModelsAsync(CancellationToken cancellationToken);

    Task<RunningOllamaModel[]> GetRunningModelsAsync(CancellationToken cancellationToken);

    Task LoadModelAsync(string model, string keepAlive, CancellationToken cancellationToken);

    Task UnloadModelAsync(string model, CancellationToken cancellationToken);

    Task<ChatResult> ChatAsync(string model, string prompt, CancellationToken cancellationToken);

    Task<ToolProbeResult> ProbeToolsAsync(string model, CancellationToken cancellationToken);
}
