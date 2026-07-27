using System.Text.Json.Serialization;
using Ollamactl.Application.Diagnostics;
using Ollamactl.Application.Logging;
using Ollamactl.Application.Processes;
using Ollamactl.Application.Server;
using Ollamactl.Domain.Models;

namespace Ollamactl.Cli.Runtime;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    WriteIndented = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    UseStringEnumConverter = true)]
[JsonSerializable(typeof(OllamaStatus))]
[JsonSerializable(typeof(OllamaModel[]))]
[JsonSerializable(typeof(RunningOllamaModel[]))]
[JsonSerializable(typeof(ChatResult))]
[JsonSerializable(typeof(ToolProbeResult))]
[JsonSerializable(typeof(ServerStartResult))]
[JsonSerializable(typeof(ServerLaunchResult))]
[JsonSerializable(typeof(ServerStopResult))]
[JsonSerializable(typeof(ManagedServerStatus))]
[JsonSerializable(typeof(OllamaProcessInfo[]))]
[JsonSerializable(typeof(OllamaLogTail[]))]
[JsonSerializable(typeof(OllamaHealthReport))]
[JsonSerializable(typeof(ConfigurationView))]
[JsonSerializable(typeof(EnvironmentOriginView[]))]
[JsonSerializable(typeof(CliError))]
[JsonSerializable(typeof(ModelActionResult))]
[JsonSerializable(typeof(ServerRestartView))]
internal sealed partial class CliJsonContext : JsonSerializerContext;
