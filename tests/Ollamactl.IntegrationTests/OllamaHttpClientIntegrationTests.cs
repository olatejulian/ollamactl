using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Ollamactl.Application.Exceptions;
using Ollamactl.Infrastructure.Http;
using Ollamactl.TestKit;
using Xunit;

namespace Ollamactl.IntegrationTests;

[Trait("Category", "Integration")]
public sealed class OllamaHttpClientIntegrationTests
{
    [Fact]
    public async Task StatusEndpointsMapResponsesAndSendExpectedRequests()
    {
        await using var server = await FakeOllamaServer.StartAsync();
        using var httpClient = CreateHttpClient(server);
        using var client = new OllamaHttpClient(httpClient);

        var version = await client.GetVersionAsync(CancellationToken.None);
        var models = await client.GetModelsAsync(CancellationToken.None);
        var runningModels = await client.GetRunningModelsAsync(CancellationToken.None);

        Assert.Equal(server.BaseUri, client.Endpoint);
        Assert.Equal(FakeOllamaServer.TestVersion, version);

        var model = Assert.Single(models);
        Assert.Equal(FakeOllamaServer.TestModel, model.Name);
        Assert.Equal(2_019_393_189, model.SizeBytes);
        Assert.Equal("sha256:test-digest", model.Digest);
        Assert.Equal(new DateTimeOffset(2026, 7, 1, 12, 0, 0, TimeSpan.Zero), model.ModifiedAt);
        Assert.Equal("llama", model.Family);
        Assert.Equal("3B", model.ParameterSize);
        Assert.Equal("Q4_K_M", model.QuantizationLevel);

        var runningModel = Assert.Single(runningModels);
        Assert.Equal(FakeOllamaServer.TestModel, runningModel.Name);
        Assert.Equal(2_019_393_189, runningModel.SizeBytes);
        Assert.Equal(1_879_048_192, runningModel.SizeVramBytes);
        Assert.Equal(new DateTimeOffset(2026, 7, 1, 12, 5, 0, TimeSpan.Zero), runningModel.ExpiresAt);

        Assert.Collection(
            server.Requests,
            request => AssertRequest(request, HttpMethod.Get, "/api/version"),
            request => AssertRequest(request, HttpMethod.Get, "/api/tags"),
            request => AssertRequest(request, HttpMethod.Get, "/api/ps"));
    }

    [Fact]
    public async Task ChatAsyncMapsResponseAndSendsNonStreamingUserMessage()
    {
        await using var server = await FakeOllamaServer.StartAsync();
        using var httpClient = CreateHttpClient(server);
        using var client = new OllamaHttpClient(httpClient);

        var result = await client.ChatAsync(
            FakeOllamaServer.TestModel,
            "Say hello",
            CancellationToken.None);

        Assert.Equal(FakeOllamaServer.TestModel, result.Model);
        Assert.Equal(FakeOllamaServer.TestChatContent, result.Content);

        var request = Assert.Single(server.GetRequests("/api/chat"));
        AssertRequest(request, HttpMethod.Post, "/api/chat");
        using var body = JsonDocument.Parse(request.Body);
        Assert.Equal(FakeOllamaServer.TestModel, body.RootElement.GetProperty("model").GetString());
        Assert.False(body.RootElement.GetProperty("stream").GetBoolean());
        Assert.False(body.RootElement.TryGetProperty("tools", out _));
        var message = Assert.Single(body.RootElement.GetProperty("messages").EnumerateArray());
        Assert.Equal("user", message.GetProperty("role").GetString());
        Assert.Equal("Say hello", message.GetProperty("content").GetString());
    }

    [Fact]
    public async Task ProbeToolsAsyncMapsToolCallAndSendsToolSchema()
    {
        await using var server = await FakeOllamaServer.StartAsync();
        using var httpClient = CreateHttpClient(server);
        using var client = new OllamaHttpClient(httpClient);

        var result = await client.ProbeToolsAsync(
            FakeOllamaServer.TestModel,
            CancellationToken.None);

        Assert.Equal(FakeOllamaServer.TestModel, result.Model);
        Assert.True(result.CalledTool);
        Assert.Equal("get_weather", result.ToolName);
        Assert.NotNull(result.ArgumentsJson);
        Assert.True(result.ElapsedMilliseconds >= 0);

        using (var arguments = JsonDocument.Parse(result.ArgumentsJson))
        {
            Assert.Equal("Sao Paulo", arguments.RootElement.GetProperty("city").GetString());
        }

        var request = Assert.Single(server.GetRequests("/api/chat"));
        using var body = JsonDocument.Parse(request.Body);
        var tool = Assert.Single(body.RootElement.GetProperty("tools").EnumerateArray());
        var function = tool.GetProperty("function");
        Assert.Equal("function", tool.GetProperty("type").GetString());
        Assert.Equal("get_weather", function.GetProperty("name").GetString());
        var parameters = function.GetProperty("parameters");
        Assert.Equal("object", parameters.GetProperty("type").GetString());
        Assert.True(parameters.GetProperty("properties").TryGetProperty("city", out _));
        Assert.Equal("city", Assert.Single(parameters.GetProperty("required").EnumerateArray()).GetString());
    }

    [Fact]
    public async Task LoadAndUnloadModelSendExpectedGenerateRequests()
    {
        await using var server = await FakeOllamaServer.StartAsync();
        using var httpClient = CreateHttpClient(server);
        using var client = new OllamaHttpClient(httpClient);

        await client.LoadModelAsync(
            FakeOllamaServer.TestModel,
            "15m",
            CancellationToken.None);
        await client.UnloadModelAsync(
            FakeOllamaServer.TestModel,
            CancellationToken.None);

        var requests = server.GetRequests("/api/generate");
        Assert.Equal(2, requests.Length);
        AssertGenerateRequest(requests[0], "15m");
        AssertGenerateRequest(requests[1], "0");
    }

    [Fact]
    public async Task HttpServerErrorThrowsClientExceptionWithStatusAndResponseBody()
    {
        await using var server = await FakeOllamaServer.StartAsync();
        server.EnqueueResponse(
            "/api/version",
            HttpStatusCode.ServiceUnavailable,
            """{"error":"planned outage"}""");
        using var httpClient = CreateHttpClient(server);
        using var client = new OllamaHttpClient(httpClient);

        var exception = await Assert.ThrowsAsync<OllamaClientException>(
            () => client.GetVersionAsync(CancellationToken.None));

        Assert.Contains("HTTP 503", exception.Message, StringComparison.Ordinal);
        Assert.Contains("planned outage", exception.Message, StringComparison.Ordinal);
        Assert.Single(server.GetRequests("/api/version"));
    }

    [Fact]
    public async Task InvalidJsonThrowsClientExceptionWithJsonCause()
    {
        await using var server = await FakeOllamaServer.StartAsync();
        server.EnqueueResponse("/api/tags", HttpStatusCode.OK, "{ definitely-not-json");
        using var httpClient = CreateHttpClient(server);
        using var client = new OllamaHttpClient(httpClient);

        var exception = await Assert.ThrowsAsync<OllamaClientException>(
            () => client.GetModelsAsync(CancellationToken.None));

        Assert.Equal("The Ollama API returned invalid JSON.", exception.Message);
        Assert.IsType<JsonException>(exception.InnerException);
        Assert.Single(server.GetRequests("/api/tags"));
    }

    [Fact]
    public async Task ResponseTimeoutIsWrappedInClientException()
    {
        await using var server = DelayedLoopbackServer.Start(TimeSpan.FromSeconds(5));
        using var httpClient = CreateHttpClient(server.BaseUri, TimeSpan.FromMilliseconds(500));
        using var client = new OllamaHttpClient(httpClient);

        var exception = await Assert.ThrowsAsync<OllamaClientException>(
            () => client.GetVersionAsync(CancellationToken.None));

        Assert.Contains("Timed out connecting to Ollama", exception.Message, StringComparison.Ordinal);
        Assert.IsAssignableFrom<OperationCanceledException>(exception.InnerException);
    }

    [Fact]
    public async Task CallerCancellationPropagatesOperationCanceledException()
    {
        await using var server = DelayedLoopbackServer.Start(TimeSpan.FromSeconds(5));
        using var httpClient = CreateHttpClient(server.BaseUri, TimeSpan.FromSeconds(10));
        using var client = new OllamaHttpClient(httpClient);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(250));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => client.GetVersionAsync(cancellation.Token));

        Assert.True(cancellation.IsCancellationRequested);
    }

    [Fact]
    public async Task RefusedConnectionIsWrappedInClientException()
    {
        using var endpoint = UnavailableLoopbackEndpoint.Create();
        using var httpClient = CreateHttpClient(endpoint.BaseUri, TimeSpan.FromSeconds(5));
        using var client = new OllamaHttpClient(httpClient);

        var exception = await Assert.ThrowsAsync<OllamaClientException>(
            () => client.GetVersionAsync(CancellationToken.None));

        Assert.Contains("Could not connect to Ollama", exception.Message, StringComparison.Ordinal);
        Assert.IsType<HttpRequestException>(exception.InnerException);
    }

    private static HttpClient CreateHttpClient(FakeOllamaServer server) =>
        CreateHttpClient(server.BaseUri, TimeSpan.FromSeconds(5));

    private static HttpClient CreateHttpClient(Uri baseUri, TimeSpan timeout) =>
        new()
        {
            BaseAddress = baseUri,
            Timeout = timeout,
        };

    private static void AssertGenerateRequest(CapturedOllamaRequest request, string expectedKeepAlive)
    {
        AssertRequest(request, HttpMethod.Post, "/api/generate");
        using var body = JsonDocument.Parse(request.Body);
        Assert.Equal(FakeOllamaServer.TestModel, body.RootElement.GetProperty("model").GetString());
        Assert.Equal(string.Empty, body.RootElement.GetProperty("prompt").GetString());
        Assert.False(body.RootElement.GetProperty("stream").GetBoolean());
        Assert.Equal(expectedKeepAlive, body.RootElement.GetProperty("keep_alive").GetString());
    }

    private static void AssertRequest(
        CapturedOllamaRequest request,
        HttpMethod expectedMethod,
        string expectedPath)
    {
        Assert.Equal(expectedMethod.Method, request.Method);
        Assert.Equal(expectedPath, request.Path);
        Assert.Equal(string.Empty, request.QueryString);
    }

    private sealed class DelayedLoopbackServer : IAsyncDisposable
    {
        private readonly CancellationTokenSource shutdown = new();
        private readonly TcpListener listener;
        private readonly TimeSpan responseDelay;
        private readonly Task serverTask;

        private DelayedLoopbackServer(TcpListener listener, Uri baseUri, TimeSpan responseDelay)
        {
            this.listener = listener;
            BaseUri = baseUri;
            this.responseDelay = responseDelay;
            serverTask = ServeOnceAsync();
        }

        public Uri BaseUri { get; }

        public static DelayedLoopbackServer Start(TimeSpan responseDelay)
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start(backlog: 1);
            var endpoint = (IPEndPoint)listener.LocalEndpoint;
            return new DelayedLoopbackServer(listener, CreateBaseUri(endpoint), responseDelay);
        }

        public async ValueTask DisposeAsync()
        {
            await shutdown.CancelAsync();
            listener.Stop();
            await serverTask;
            shutdown.Dispose();
            GC.SuppressFinalize(this);
        }

        private async Task ServeOnceAsync()
        {
            try
            {
                using var client = await listener.AcceptTcpClientAsync(shutdown.Token);
                await Task.Delay(responseDelay, shutdown.Token);
                await using var stream = client.GetStream();
                var response = Encoding.ASCII.GetBytes(
                    "HTTP/1.1 200 OK\r\nContent-Type: application/json\r\nContent-Length: 21\r\nConnection: close\r\n\r\n{\"version\":\"delayed\"}");
                await stream.WriteAsync(response, shutdown.Token);
            }
            catch (OperationCanceledException) when (shutdown.IsCancellationRequested)
            {
                // Normal disposal while the deliberately delayed response is pending.
            }
            catch (SocketException) when (shutdown.IsCancellationRequested)
            {
                // TcpListener.Stop can abort a pending accept during disposal.
            }
        }
    }

    private sealed class UnavailableLoopbackEndpoint : IDisposable
    {
        private readonly Socket socket;

        private UnavailableLoopbackEndpoint(Socket socket, Uri baseUri)
        {
            this.socket = socket;
            BaseUri = baseUri;
        }

        public Uri BaseUri { get; }

        public static UnavailableLoopbackEndpoint Create()
        {
            var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            socket.Bind(new IPEndPoint(IPAddress.Loopback, 0));
            var endpoint = (IPEndPoint)socket.LocalEndPoint!;
            return new UnavailableLoopbackEndpoint(socket, CreateBaseUri(endpoint));
        }

        public void Dispose()
        {
            socket.Dispose();
            GC.SuppressFinalize(this);
        }
    }

    private static Uri CreateBaseUri(IPEndPoint endpoint) =>
        new UriBuilder(Uri.UriSchemeHttp, endpoint.Address.ToString(), endpoint.Port).Uri;
}
