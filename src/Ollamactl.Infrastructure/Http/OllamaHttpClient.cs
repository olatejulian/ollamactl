using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Ollamactl.Application.Abstractions;
using Ollamactl.Application.Exceptions;
using Ollamactl.Domain.Models;

namespace Ollamactl.Infrastructure.Http;

public sealed class OllamaHttpClient : IOllamaApiClient
{
    private readonly HttpClient httpClient;
    private readonly bool ownsHttpClient;

    public OllamaHttpClient(HttpClient httpClient, bool ownsHttpClient = false)
    {
        this.httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        if (httpClient.BaseAddress is null)
        {
            throw new ArgumentException("HttpClient.BaseAddress must be configured.", nameof(httpClient));
        }

        Endpoint = httpClient.BaseAddress;
        this.ownsHttpClient = ownsHttpClient;
    }

    public Uri Endpoint { get; }

    public async Task<string> GetVersionAsync(CancellationToken cancellationToken)
    {
        var response = await GetAsync("api/version", OllamaJsonContext.Default.VersionResponse, cancellationToken)
            .ConfigureAwait(false);

        return !string.IsNullOrWhiteSpace(response.Version)
            ? response.Version
            : throw InvalidResponse("The Ollama version response did not contain a version.");
    }

    public async Task<OllamaModel[]> GetModelsAsync(CancellationToken cancellationToken)
    {
        var response = await GetAsync("api/tags", OllamaJsonContext.Default.TagsResponse, cancellationToken)
            .ConfigureAwait(false);

        return (response.Models ?? [])
            .Where(model => !string.IsNullOrWhiteSpace(model.Name))
            .Select(model => new OllamaModel(
                model.Name!,
                model.Size,
                model.Digest,
                model.ModifiedAt,
                model.Details?.Family,
                model.Details?.ParameterSize,
                model.Details?.QuantizationLevel))
            .ToArray();
    }

    public async Task<RunningOllamaModel[]> GetRunningModelsAsync(CancellationToken cancellationToken)
    {
        var response = await GetAsync("api/ps", OllamaJsonContext.Default.RunningModelsResponse, cancellationToken)
            .ConfigureAwait(false);

        return (response.Models ?? [])
            .Where(model => !string.IsNullOrWhiteSpace(model.Name))
            .Select(model => new RunningOllamaModel(
                model.Name!,
                model.Size,
                model.SizeVram,
                model.ExpiresAt))
            .ToArray();
    }

    public Task LoadModelAsync(string model, string keepAlive, CancellationToken cancellationToken) =>
        PostWithoutResultAsync(
            "api/generate",
            new GenerateRequest(model, string.Empty, Stream: false, keepAlive),
            OllamaJsonContext.Default.GenerateRequest,
            cancellationToken);

    public Task UnloadModelAsync(string model, CancellationToken cancellationToken) =>
        PostWithoutResultAsync(
            "api/generate",
            new GenerateRequest(model, string.Empty, Stream: false, KeepAlive: "0"),
            OllamaJsonContext.Default.GenerateRequest,
            cancellationToken);

    public async Task<ChatResult> ChatAsync(string model, string prompt, CancellationToken cancellationToken)
    {
        var request = new ChatRequest(
            model,
            Stream: false,
            [new ChatMessage("user", prompt)]);

        var response = await PostAsync(
                "api/chat",
                request,
                OllamaJsonContext.Default.ChatRequest,
                OllamaJsonContext.Default.ChatResponse,
                cancellationToken)
            .ConfigureAwait(false);

        if (response.Message?.Content is not { } content)
        {
            throw InvalidResponse("The Ollama chat response did not contain message content.");
        }

        return new ChatResult(response.Model ?? model, content);
    }

    public async Task<ToolProbeResult> ProbeToolsAsync(string model, CancellationToken cancellationToken)
    {
        var request = new ChatRequest(
            model,
            Stream: false,
            [new ChatMessage("user", "What is the weather in Sao Paulo? Use the get_weather tool.")],
            [
                new ToolDefinition(
                    "function",
                    new ToolFunction(
                        "get_weather",
                        "Return the current weather for a city.",
                        new ToolParameters(
                            "object",
                            new Dictionary<string, ToolProperty>(StringComparer.Ordinal)
                            {
                                ["city"] = new("string", "City name"),
                            },
                            ["city"])))
            ]);

        var stopwatch = Stopwatch.StartNew();
        var response = await PostAsync(
                "api/chat",
                request,
                OllamaJsonContext.Default.ChatRequest,
                OllamaJsonContext.Default.ChatResponse,
                cancellationToken)
            .ConfigureAwait(false);
        stopwatch.Stop();

        var call = response.Message?.ToolCalls?.FirstOrDefault()?.Function;
        var arguments = call?.Arguments.ValueKind is not (JsonValueKind.Undefined or JsonValueKind.Null)
            ? call?.Arguments.GetRawText()
            : null;

        return new ToolProbeResult(
            response.Model ?? model,
            call is not null,
            call?.Name,
            arguments,
            stopwatch.ElapsedMilliseconds);
    }

    public void Dispose()
    {
        if (ownsHttpClient)
        {
            httpClient.Dispose();
        }
    }

    private async Task<TResponse> GetAsync<TResponse>(
        string path,
        JsonTypeInfo<TResponse> responseType,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        return await SendAsync(request, responseType, cancellationToken).ConfigureAwait(false);
    }

    private async Task<TResponse> PostAsync<TRequest, TResponse>(
        string path,
        TRequest body,
        JsonTypeInfo<TRequest> requestType,
        JsonTypeInfo<TResponse> responseType,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = JsonContent.Create(body, requestType),
        };

        return await SendAsync(request, responseType, cancellationToken).ConfigureAwait(false);
    }

    private async Task PostWithoutResultAsync<TRequest>(
        string path,
        TRequest body,
        JsonTypeInfo<TRequest> requestType,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = JsonContent.Create(body, requestType),
        };

        using var response = await SendCoreAsync(request, cancellationToken).ConfigureAwait(false);
    }

    private async Task<TResponse> SendAsync<TResponse>(
        HttpRequestMessage request,
        JsonTypeInfo<TResponse> responseType,
        CancellationToken cancellationToken)
    {
        using var response = await SendCoreAsync(request, cancellationToken).ConfigureAwait(false);

        try
        {
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            return await JsonSerializer.DeserializeAsync(stream, responseType, cancellationToken).ConfigureAwait(false)
                ?? throw InvalidResponse("The Ollama API returned an empty JSON response.");
        }
        catch (JsonException exception)
        {
            throw new OllamaClientException("The Ollama API returned invalid JSON.", exception);
        }
    }

    private async Task<HttpResponseMessage> SendCoreAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await httpClient.SendAsync(
                    request,
                    HttpCompletionOption.ResponseHeadersRead,
                    cancellationToken)
                .ConfigureAwait(false);

            if (response.IsSuccessStatusCode)
            {
                return response;
            }

            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            var statusCode = response.StatusCode;
            var reasonPhrase = response.ReasonPhrase;
            response.Dispose();
            var detail = string.IsNullOrWhiteSpace(body) ? string.Empty : $" Response: {Truncate(body, 512)}";
            throw new OllamaClientException(
                $"Ollama API returned HTTP {(int)statusCode} ({reasonPhrase}).{detail}");
        }
        catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            throw new OllamaClientException($"Timed out connecting to Ollama at {Endpoint}.", exception);
        }
        catch (HttpRequestException exception)
        {
            throw new OllamaClientException($"Could not connect to Ollama at {Endpoint}.", exception);
        }
    }

    private static OllamaClientException InvalidResponse(string message) => new(message);

    private static string Truncate(string value, int maximumLength) =>
        value.Length <= maximumLength ? value : string.Concat(value.AsSpan(0, maximumLength), "...");
}
