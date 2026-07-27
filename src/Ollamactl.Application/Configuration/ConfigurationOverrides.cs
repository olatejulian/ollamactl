namespace Ollamactl.Application.Configuration;

public sealed record ConfigurationOverrides(
    string? Host = null,
    int? TimeoutSeconds = null,
    string? ConfigDirectory = null,
    string? EnvFile = null);
