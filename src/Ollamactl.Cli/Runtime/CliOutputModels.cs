namespace Ollamactl.Cli.Runtime;

public sealed record ConfigurationView(
    string Host,
    int TimeoutSeconds,
    string HostSource,
    string TimeoutSource,
    string ConfigDirectory,
    string EnvFile,
    string ServerStateFile,
    string? OllamaExecutable);

public sealed record EnvironmentOriginView(string Name, string Source);

public sealed record CliError(int ExitCode, string Error);

public sealed record ModelActionResult(string Model, string Status);

public sealed record ServerRestartView(
    Application.Server.ServerStopResult Stop,
    Application.Server.ServerLaunchResult Start);
