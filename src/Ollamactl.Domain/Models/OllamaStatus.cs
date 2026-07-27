namespace Ollamactl.Domain.Models;

public sealed record OllamaStatus(
    Uri Endpoint,
    string Version,
    IReadOnlyList<OllamaModel> Models,
    IReadOnlyList<RunningOllamaModel> RunningModels);
