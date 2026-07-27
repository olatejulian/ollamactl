using Ollamactl.Application.Abstractions;
using Ollamactl.Domain.Models;

namespace Ollamactl.Application.Services;

public sealed class OllamaService(IOllamaApiClient client)
{
    public async Task<OllamaStatus> GetStatusAsync(CancellationToken cancellationToken)
    {
        var versionTask = client.GetVersionAsync(cancellationToken);
        var modelsTask = client.GetModelsAsync(cancellationToken);
        var runningModelsTask = client.GetRunningModelsAsync(cancellationToken);

        await Task.WhenAll(versionTask, modelsTask, runningModelsTask).ConfigureAwait(false);

        return new OllamaStatus(
            client.Endpoint,
            await versionTask.ConfigureAwait(false),
            await modelsTask.ConfigureAwait(false),
            await runningModelsTask.ConfigureAwait(false));
    }

    public Task<OllamaModel[]> GetModelsAsync(CancellationToken cancellationToken) =>
        client.GetModelsAsync(cancellationToken);

    public Task<RunningOllamaModel[]> GetRunningModelsAsync(CancellationToken cancellationToken) =>
        client.GetRunningModelsAsync(cancellationToken);

    public Task LoadModelAsync(string model, string keepAlive, CancellationToken cancellationToken) =>
        client.LoadModelAsync(model, keepAlive, cancellationToken);

    public Task UnloadModelAsync(string model, CancellationToken cancellationToken) =>
        client.UnloadModelAsync(model, cancellationToken);

    public Task<ChatResult> ChatAsync(string model, string prompt, CancellationToken cancellationToken) =>
        client.ChatAsync(model, prompt, cancellationToken);

    public Task<ToolProbeResult> ProbeToolsAsync(string model, CancellationToken cancellationToken) =>
        client.ProbeToolsAsync(model, cancellationToken);
}
