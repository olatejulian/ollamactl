namespace Ollamactl.Application.Server;

public sealed record ServerLaunchResult(
    bool StartedByOllamactl,
    bool AlreadyAvailable,
    ServerStartResult? Process,
    string ApiVersion,
    long ReadinessMilliseconds);
