namespace Ollamactl.Infrastructure.Processes;

internal sealed record ServerProcessState(
    int SchemaVersion,
    string Status,
    int SupervisorProcessId,
    string SupervisorExecutable,
    DateTimeOffset SupervisorStartedAtUtc,
    int? TargetProcessId,
    string TargetExecutable,
    DateTimeOffset? TargetStartedAtUtc,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? ExitedAtUtc,
    int? ExitCode)
{
    public const int CurrentSchemaVersion = 1;
    public const string StartingStatus = "starting";
    public const string RunningStatus = "running";
    public const string ExitedStatus = "exited";
}
