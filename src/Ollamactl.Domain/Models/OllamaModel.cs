namespace Ollamactl.Domain.Models;

public sealed record OllamaModel(
    string Name,
    long SizeBytes,
    string? Digest,
    DateTimeOffset? ModifiedAt,
    string? Family,
    string? ParameterSize,
    string? QuantizationLevel);
