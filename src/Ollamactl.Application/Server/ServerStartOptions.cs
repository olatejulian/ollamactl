namespace Ollamactl.Application.Server;

public sealed record ServerStartOptions(
    string ConfigDirectory,
    string OllamaExecutable,
    IReadOnlyDictionary<string, string> EnvironmentVariables);
