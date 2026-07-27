using Ollamactl.Application.Abstractions;
using Ollamactl.Application.Configuration;

namespace Ollamactl.Infrastructure.Http;

public sealed class OllamaHttpClientFactory : IOllamaApiClientFactory
{
    public IOllamaApiClient Create(ResolvedOllamaConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var httpClient = new HttpClient
        {
            BaseAddress = configuration.Endpoint,
            Timeout = configuration.Timeout,
        };

        var version = typeof(OllamaHttpClientFactory).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";
        httpClient.DefaultRequestHeaders.UserAgent.ParseAdd($"ollamactl/{version}");
        return new OllamaHttpClient(httpClient, ownsHttpClient: true);
    }
}
