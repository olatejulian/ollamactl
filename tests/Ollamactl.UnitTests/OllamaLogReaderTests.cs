using Ollamactl.Application.Logging;
using Ollamactl.Infrastructure.Configuration;
using Ollamactl.Infrastructure.Logging;

namespace Ollamactl.UnitTests;

[Trait("Category", "Unit")]
public sealed class OllamaLogReaderTests
{
    [Fact]
    public async Task ReadTailLimitsEachFileAndRedactsSecretsBeforeReturningLines()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var resolver = new OllamactlPathResolver();
        var paths = resolver.Resolve(temporaryDirectory.Path);
        Directory.CreateDirectory(paths.LogDirectory);
        await File.WriteAllLinesAsync(
            paths.StandardOutputLog,
            [
                "discarded first line",
                "discarded second line",
                "warning token=top-secret-token",
                "ERROR Authorization: Bearer abc.def.secret",
                "request url=https://admin:password@example.test/api",
            ]);

        var officialLog = Path.Combine(temporaryDirectory.Path, "official", "server.log");
        Directory.CreateDirectory(Path.GetDirectoryName(officialLog)!);
        await File.WriteAllLinesAsync(
            officialLog,
            [
                "OLLAMA_API_KEY=sk-1234567890abcdefghijkl",
                "{\"password\": \"don't-leak-this\"}",
                "Authorization: Basic dXNlcjpwYXNzd29yZA==",
            ]);
        var reader = new OllamaLogReader(resolver, [officialLog]);

        var result = await reader.ReadTailAsync(
            temporaryDirectory.Path,
            maximumLines: 3,
            CancellationToken.None);

        var managed = Assert.Single(
            result,
            log => log.Source == OllamaLogSource.ManagedStandardOutput);
        Assert.Equal([3L, 4L, 5L], managed.Lines.Select(line => line.LineNumber));
        Assert.All(managed.Lines, line => Assert.True(line.IsSuspicious));
        Assert.All(managed.Lines, line => Assert.True(line.WasRedacted));

        var official = Assert.Single(result, log => log.Source == OllamaLogSource.Official);
        Assert.Equal(3, official.Lines.Count);
        Assert.All(official.Lines, line => Assert.True(line.WasRedacted));

        var exposedText = string.Join('\n', result.SelectMany(log => log.Lines).Select(line => line.Text));
        Assert.DoesNotContain("top-secret-token", exposedText, StringComparison.Ordinal);
        Assert.DoesNotContain("abc.def.secret", exposedText, StringComparison.Ordinal);
        Assert.DoesNotContain("admin:password", exposedText, StringComparison.Ordinal);
        Assert.DoesNotContain("sk-1234567890abcdefghijkl", exposedText, StringComparison.Ordinal);
        Assert.DoesNotContain("don't-leak-this", exposedText, StringComparison.Ordinal);
        Assert.DoesNotContain("dXNlcjpwYXNzd29yZA==", exposedText, StringComparison.Ordinal);
        Assert.Contains("[REDACTED]", exposedText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReadTailSkipsMissingAndUnreadableCandidatesBestEffort()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var resolver = new OllamactlPathResolver();
        var paths = resolver.Resolve(temporaryDirectory.Path);
        Directory.CreateDirectory(paths.LogDirectory);
        await File.WriteAllTextAsync(paths.StandardOutputLog, "healthy line");
        await File.WriteAllTextAsync(paths.StandardErrorLog, "locked line");
        await using var lockedLog = new FileStream(
            paths.StandardErrorLog,
            FileMode.Open,
            FileAccess.ReadWrite,
            FileShare.None,
            bufferSize: 4096,
            useAsync: true);
        var missingOfficialLog = Path.Combine(temporaryDirectory.Path, "missing", "server.log");
        var reader = new OllamaLogReader(resolver, [missingOfficialLog]);

        var result = await reader.ReadTailAsync(
            temporaryDirectory.Path,
            maximumLines: 10,
            CancellationToken.None);

        var log = Assert.Single(result);
        Assert.Equal(OllamaLogSource.ManagedStandardOutput, log.Source);
        Assert.Equal("healthy line", Assert.Single(log.Lines).Text);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(OllamaLogReader.MaximumTailLines + 1)]
    public async Task ReadTailRejectsRequestsOutsideTheBoundedRange(int maximumLines)
    {
        var reader = new OllamaLogReader(new OllamactlPathResolver(), []);

        var exception = await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            reader.ReadTailAsync(null, maximumLines, CancellationToken.None));

        Assert.Equal("maximumLines", exception.ParamName);
    }
}
