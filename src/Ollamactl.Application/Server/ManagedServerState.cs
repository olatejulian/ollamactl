namespace Ollamactl.Application.Server;

public enum ManagedServerState
{
    NotManaged,
    Starting,
    Running,
    Exited,
    Stale,
}
