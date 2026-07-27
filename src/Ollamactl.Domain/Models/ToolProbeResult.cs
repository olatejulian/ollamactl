namespace Ollamactl.Domain.Models;

public sealed record ToolProbeResult(
    string Model,
    bool CalledTool,
    string? ToolName,
    string? ArgumentsJson,
    long ElapsedMilliseconds);
