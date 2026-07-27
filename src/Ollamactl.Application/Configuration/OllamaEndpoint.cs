namespace Ollamactl.Application.Configuration;

public static class OllamaEndpoint
{
    public const string DefaultHost = "127.0.0.1:11434";

    public static Uri Parse(string? value)
    {
        var host = string.IsNullOrWhiteSpace(value) ? DefaultHost : value.Trim();
        if (!host.Contains("://", StringComparison.Ordinal))
        {
            host = $"http://{host}";
        }

        if (!Uri.TryCreate(host, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
            || string.IsNullOrWhiteSpace(uri.Host)
            || !string.IsNullOrEmpty(uri.Query)
            || !string.IsNullOrEmpty(uri.Fragment)
            || uri.AbsolutePath != "/")
        {
            throw new FormatException($"Invalid Ollama host '{value}'. Use host:port or an HTTP(S) URL without a path.");
        }

        var builder = new UriBuilder(uri) { Path = "/" };
        return builder.Uri;
    }
}
