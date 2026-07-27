using Ollamactl.Application.Server;

namespace Ollamactl.Application.Abstractions;

public interface IOllamaServerManager
{
    Task<ServerStartResult> StartAsync(ServerStartOptions options, CancellationToken cancellationToken);

    Task<ServerStopResult> StopAsync(string configDirectory, CancellationToken cancellationToken);

    Task<ManagedServerStatus> GetStatusAsync(string configDirectory, CancellationToken cancellationToken);
}
