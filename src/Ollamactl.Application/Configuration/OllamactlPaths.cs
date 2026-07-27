namespace Ollamactl.Application.Configuration;

public sealed record OllamactlPaths(
    string ConfigDirectory,
    string EnvFile,
    string LogDirectory,
    string RunDirectory,
    string ServerStateFile,
    string StandardOutputLog,
    string StandardErrorLog);
