namespace Ollamactl.Application.Configuration;

public sealed record ResolvedExecutionContext(
    ResolvedOllamaConfiguration Configuration,
    string EnvironmentFile,
    IReadOnlyDictionary<string, string> EffectiveEnvironment,
    IReadOnlyDictionary<string, string> Origins);
