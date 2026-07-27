namespace Ollamactl.Domain.Models;

public sealed record RunningOllamaModel(
    string Name,
    long SizeBytes,
    long SizeVramBytes,
    DateTimeOffset? ExpiresAt);
