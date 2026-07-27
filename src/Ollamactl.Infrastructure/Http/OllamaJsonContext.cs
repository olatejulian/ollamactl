using System.Text.Json.Serialization;

namespace Ollamactl.Infrastructure.Http;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(VersionResponse))]
[JsonSerializable(typeof(TagsResponse))]
[JsonSerializable(typeof(RunningModelsResponse))]
[JsonSerializable(typeof(GenerateRequest))]
[JsonSerializable(typeof(ChatRequest))]
[JsonSerializable(typeof(ChatResponse))]
internal sealed partial class OllamaJsonContext : JsonSerializerContext;
