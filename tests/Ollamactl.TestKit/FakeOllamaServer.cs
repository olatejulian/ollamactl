using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Ollamactl.TestKit;

/// <summary>
/// A deterministic, in-process Ollama HTTP server that listens on a real loopback socket.
/// </summary>
public sealed class FakeOllamaServer : IAsyncDisposable
{
    public const string TestVersion = "0.5.7-test";
    public const string TestModel = "llama3.2:latest";
    public const string TestChatContent = "Hello from fake Ollama.";

    private const string RequestBodyItemKey = "Ollamactl.TestKit.RequestBody";
    private const string JsonContentType = "application/json; charset=utf-8";

    private readonly WebApplication application;
    private readonly ConcurrentQueue<CapturedOllamaRequest> capturedRequests;
    private readonly ConcurrentDictionary<string, ConcurrentQueue<PlannedResponse>> plannedResponses;
    private int disposed;

    private FakeOllamaServer(
        WebApplication application,
        Uri baseUri,
        ConcurrentQueue<CapturedOllamaRequest> capturedRequests,
        ConcurrentDictionary<string, ConcurrentQueue<PlannedResponse>> plannedResponses)
    {
        this.application = application;
        BaseUri = baseUri;
        this.capturedRequests = capturedRequests;
        this.plannedResponses = plannedResponses;
    }

    public Uri BaseUri { get; }

    public IReadOnlyList<CapturedOllamaRequest> Requests => capturedRequests.ToArray();

    public static async Task<FakeOllamaServer> StartAsync(CancellationToken cancellationToken = default)
    {
        var capturedRequests = new ConcurrentQueue<CapturedOllamaRequest>();
        var plannedResponses = new ConcurrentDictionary<string, ConcurrentQueue<PlannedResponse>>(
            StringComparer.Ordinal);

        var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions
        {
            ApplicationName = typeof(FakeOllamaServer).Assembly.GetName().Name,
            Args = [],
            EnvironmentName = Environments.Development,
        });

        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(static options => options.Listen(IPAddress.Loopback, 0));

        var application = builder.Build();
        ConfigurePipeline(application, capturedRequests, plannedResponses);

        try
        {
            await application.StartAsync(cancellationToken).ConfigureAwait(false);
            var baseUri = ResolveBaseUri(application);
            return new FakeOllamaServer(application, baseUri, capturedRequests, plannedResponses);
        }
        catch
        {
            await application.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    public void EnqueueResponse(
        string path,
        HttpStatusCode statusCode,
        string body,
        string contentType = JsonContentType)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(body);
        ArgumentException.ThrowIfNullOrWhiteSpace(contentType);

        var normalizedPath = path.StartsWith('/') ? path : $"/{path}";
        var responses = plannedResponses.GetOrAdd(
            normalizedPath,
            static _ => new ConcurrentQueue<PlannedResponse>());

        responses.Enqueue(new PlannedResponse((int)statusCode, body, contentType));
    }

    public CapturedOllamaRequest[] GetRequests(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var normalizedPath = path.StartsWith('/') ? path : $"/{path}";
        return capturedRequests
            .Where(request => string.Equals(request.Path, normalizedPath, StringComparison.Ordinal))
            .ToArray();
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0)
        {
            return;
        }

        await application.StopAsync().ConfigureAwait(false);
        await application.DisposeAsync().ConfigureAwait(false);
        GC.SuppressFinalize(this);
    }

    private static void ConfigurePipeline(
        WebApplication application,
        ConcurrentQueue<CapturedOllamaRequest> capturedRequests,
        ConcurrentDictionary<string, ConcurrentQueue<PlannedResponse>> plannedResponses)
    {
        application.Use(async (context, next) =>
        {
            context.Request.EnableBuffering();
            using var reader = new StreamReader(
                context.Request.Body,
                Encoding.UTF8,
                detectEncodingFromByteOrderMarks: false,
                bufferSize: 1024,
                leaveOpen: true);
            var body = await reader.ReadToEndAsync(context.RequestAborted).ConfigureAwait(false);
            context.Request.Body.Position = 0;

            capturedRequests.Enqueue(new CapturedOllamaRequest(
                context.Request.Method,
                context.Request.Path.Value ?? string.Empty,
                context.Request.QueryString.Value ?? string.Empty,
                body,
                context.Request.Headers.ToDictionary(
                    static header => header.Key,
                    static header => header.Value.Select(static value => value ?? string.Empty).ToArray(),
                    StringComparer.OrdinalIgnoreCase)));

            if (TryDequeueResponse(plannedResponses, context.Request.Path.Value, out var plannedResponse))
            {
                context.Response.StatusCode = plannedResponse.StatusCode;
                context.Response.ContentType = plannedResponse.ContentType;
                await context.Response.WriteAsync(plannedResponse.Body, context.RequestAborted).ConfigureAwait(false);
                return;
            }

            context.Items[RequestBodyItemKey] = body;
            await next(context).ConfigureAwait(false);
        });

        application.MapGet(
            "/api/version",
            static () => Results.Content(
                $$"""{"version":"{{TestVersion}}"}""",
                JsonContentType,
                Encoding.UTF8));

        application.MapGet(
            "/api/tags",
            static () => Results.Content(
                """
                {
                  "models": [
                    {
                      "name": "llama3.2:latest",
                      "size": 2019393189,
                      "digest": "sha256:test-digest",
                      "modified_at": "2026-07-01T12:00:00Z",
                      "details": {
                        "family": "llama",
                        "parameter_size": "3B",
                        "quantization_level": "Q4_K_M"
                      }
                    },
                    {
                      "name": "",
                      "size": 1
                    }
                  ]
                }
                """,
                JsonContentType,
                Encoding.UTF8));

        application.MapGet(
            "/api/ps",
            static () => Results.Content(
                """
                {
                  "models": [
                    {
                      "name": "llama3.2:latest",
                      "size": 2019393189,
                      "size_vram": 1879048192,
                      "expires_at": "2026-07-01T12:05:00Z"
                    }
                  ]
                }
                """,
                JsonContentType,
                Encoding.UTF8));

        application.MapPost(
            "/api/generate",
            static () => Results.Content("""{"done":true}""", JsonContentType, Encoding.UTF8));

        application.MapPost("/api/chat", static (HttpContext context) => CreateChatResponse(context));
    }

    private static IResult CreateChatResponse(HttpContext context)
    {
        var body = context.Items[RequestBodyItemKey] as string ?? string.Empty;
        using var document = JsonDocument.Parse(body);
        var root = document.RootElement;
        var model = root.TryGetProperty("model", out var modelElement)
            ? modelElement.GetString() ?? TestModel
            : TestModel;
        var serializedModel = JsonSerializer.Serialize(model);

        if (root.TryGetProperty("tools", out var tools) && tools.ValueKind == JsonValueKind.Array)
        {
            return Results.Content(
                $$"""
                {
                  "model": {{serializedModel}},
                  "message": {
                    "role": "assistant",
                    "content": "",
                    "tool_calls": [
                      {
                        "function": {
                          "name": "get_weather",
                          "arguments": { "city": "Sao Paulo" }
                        }
                      }
                    ]
                  }
                }
                """,
                JsonContentType,
                Encoding.UTF8);
        }

        return Results.Content(
            $$"""
            {
              "model": {{serializedModel}},
              "message": {
                "role": "assistant",
                "content": "{{TestChatContent}}"
              }
            }
            """,
            JsonContentType,
            Encoding.UTF8);
    }

    private static bool TryDequeueResponse(
        ConcurrentDictionary<string, ConcurrentQueue<PlannedResponse>> plannedResponses,
        string? path,
        out PlannedResponse response)
    {
        if (path is not null
            && plannedResponses.TryGetValue(path, out var responses)
            && responses.TryDequeue(out var queuedResponse))
        {
            response = queuedResponse;
            return true;
        }

        response = default!;
        return false;
    }

    private static Uri ResolveBaseUri(WebApplication application)
    {
        var server = application.Services.GetRequiredService<IServer>();
        var addresses = server.Features.Get<IServerAddressesFeature>()?.Addresses;
        var address = addresses?.SingleOrDefault(static value => value.StartsWith("http://", StringComparison.Ordinal));
        if (address is null)
        {
            throw new InvalidOperationException("Kestrel did not publish a loopback HTTP address.");
        }

        var uriBuilder = new UriBuilder(address) { Path = "/" };
        return uriBuilder.Uri;
    }

    private sealed record PlannedResponse(int StatusCode, string Body, string ContentType);
}
