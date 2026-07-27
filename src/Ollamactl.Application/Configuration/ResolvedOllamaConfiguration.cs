namespace Ollamactl.Application.Configuration;

public sealed record ResolvedOllamaConfiguration(
    Uri Endpoint,
    TimeSpan Timeout,
    OllamactlPaths Paths,
    string HostSource,
    string TimeoutSource);
