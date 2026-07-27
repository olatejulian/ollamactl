using System.Text.Json.Serialization;

namespace Ollamactl.Infrastructure.Processes;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, WriteIndented = true)]
[JsonSerializable(typeof(ServerProcessState))]
internal sealed partial class ProcessJsonContext : JsonSerializerContext;
