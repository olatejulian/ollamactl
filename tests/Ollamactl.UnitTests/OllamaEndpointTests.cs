using Ollamactl.Application.Configuration;

namespace Ollamactl.UnitTests;

[Trait("Category", "Unit")]
public sealed class OllamaEndpointTests
{
    [Theory]
    [InlineData(null, "http://127.0.0.1:11434/")]
    [InlineData("", "http://127.0.0.1:11434/")]
    [InlineData("   ", "http://127.0.0.1:11434/")]
    [InlineData("localhost:11434", "http://localhost:11434/")]
    [InlineData(" http://localhost:12345 ", "http://localhost:12345/")]
    [InlineData("https://example.test:8443", "https://example.test:8443/")]
    [InlineData("[::1]:11434", "http://[::1]:11434/")]
    public void ParseNormalizesSupportedHostForms(string? value, string expected)
    {
        var endpoint = OllamaEndpoint.Parse(value);

        Assert.Equal(new Uri(expected), endpoint);
        Assert.Equal("/", endpoint.AbsolutePath);
    }

    [Theory]
    [InlineData("ftp://localhost:11434")]
    [InlineData("http://localhost:11434/api")]
    [InlineData("http://localhost:11434?debug=true")]
    [InlineData("http://localhost:11434/#fragment")]
    [InlineData("http://")]
    [InlineData("not a valid host")]
    [InlineData("localhost:99999")]
    public void ParseRejectsUnsupportedOrMalformedEndpoints(string value)
    {
        var exception = Assert.Throws<FormatException>(() => OllamaEndpoint.Parse(value));

        Assert.Contains("Invalid Ollama host", exception.Message, StringComparison.Ordinal);
        Assert.Contains("without a path", exception.Message, StringComparison.Ordinal);
    }
}
