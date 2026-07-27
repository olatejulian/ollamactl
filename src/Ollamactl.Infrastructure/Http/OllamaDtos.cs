using System.Text.Json;

namespace Ollamactl.Infrastructure.Http;

internal sealed record VersionResponse(string? Version);

internal sealed record TagsResponse(ModelResponse[]? Models);

internal sealed record ModelResponse(
    string? Name,
    long Size,
    string? Digest,
    DateTimeOffset? ModifiedAt,
    ModelDetailsResponse? Details);

internal sealed record ModelDetailsResponse(
    string? Family,
    string? ParameterSize,
    string? QuantizationLevel);

internal sealed record RunningModelsResponse(RunningModelResponse[]? Models);

internal sealed record RunningModelResponse(
    string? Name,
    long Size,
    long SizeVram,
    DateTimeOffset? ExpiresAt);

internal sealed record GenerateRequest(
    string Model,
    string Prompt,
    bool Stream,
    string KeepAlive);

internal sealed record ChatRequest(
    string Model,
    bool Stream,
    ChatMessage[] Messages,
    ToolDefinition[]? Tools = null);

internal sealed record ChatMessage(string Role, string Content);

internal sealed record ToolDefinition(string Type, ToolFunction Function);

internal sealed record ToolFunction(
    string Name,
    string Description,
    ToolParameters Parameters);

internal sealed record ToolParameters(
    string Type,
    Dictionary<string, ToolProperty> Properties,
    string[] Required);

internal sealed record ToolProperty(string Type, string Description);

internal sealed record ChatResponse(ChatResponseMessage? Message, string? Model);

internal sealed record ChatResponseMessage(
    string? Role,
    string? Content,
    ToolCallResponse[]? ToolCalls);

internal sealed record ToolCallResponse(ToolCallFunctionResponse? Function);

internal sealed record ToolCallFunctionResponse(
    string? Name,
    JsonElement Arguments);
