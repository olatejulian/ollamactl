using System.Security;
using System.Text.RegularExpressions;
using Ollamactl.Application.Abstractions;
using Ollamactl.Application.Logging;
using Ollamactl.Infrastructure.Configuration;

namespace Ollamactl.Infrastructure.Logging;

public sealed partial class OllamaLogReader : IOllamaLogReader
{
    public const int MaximumTailLines = 1000;

    private const int MaximumLineLength = 16 * 1024;
    private const string RedactedValue = "[REDACTED]";
    private readonly IOllamactlPathResolver pathResolver;
    private readonly IReadOnlyList<string> officialLogPaths;

    public OllamaLogReader()
        : this(new OllamactlPathResolver())
    {
    }

    public OllamaLogReader(IOllamactlPathResolver pathResolver)
        : this(pathResolver, GetDefaultOfficialLogPaths())
    {
    }

    public OllamaLogReader(
        IOllamactlPathResolver pathResolver,
        IEnumerable<string> officialLogPaths)
    {
        this.pathResolver = pathResolver ?? throw new ArgumentNullException(nameof(pathResolver));
        ArgumentNullException.ThrowIfNull(officialLogPaths);
        this.officialLogPaths = officialLogPaths
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .ToArray();
    }

    public async Task<IReadOnlyList<OllamaLogTail>> ReadTailAsync(
        string? configDirectory,
        int maximumLines,
        CancellationToken cancellationToken)
    {
        if (maximumLines is < 1 or > MaximumTailLines)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maximumLines),
                maximumLines,
                $"The log tail must contain between 1 and {MaximumTailLines} lines.");
        }

        cancellationToken.ThrowIfCancellationRequested();
        var paths = pathResolver.Resolve(configDirectory);
        var candidates = new List<LogCandidate>
        {
            new(OllamaLogSource.ManagedStandardOutput, paths.StandardOutputLog),
            new(OllamaLogSource.ManagedStandardError, paths.StandardErrorLog),
        };
        candidates.AddRange(officialLogPaths.Select(path => new LogCandidate(OllamaLogSource.Official, path)));

        var result = new List<OllamaLogTail>();
        var visitedPaths = new HashSet<string>(GetPathComparer());
        foreach (var candidate in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var fullPath = TryGetFullPath(candidate.Path);
            if (fullPath is null || !visitedPaths.Add(fullPath))
            {
                continue;
            }

            var lines = await TryReadTailAsync(fullPath, maximumLines, cancellationToken).ConfigureAwait(false);
            if (lines is not null)
            {
                result.Add(new OllamaLogTail(candidate.Source, fullPath, lines));
            }
        }

        return result;
    }

    private static async Task<IReadOnlyList<OllamaLogLine>?> TryReadTailAsync(
        string path,
        int maximumLines,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete,
                bufferSize: 4096,
                useAsync: true);
            using var reader = new StreamReader(stream, detectEncodingFromByteOrderMarks: true);
            var tail = new Queue<OllamaLogLine>(maximumLines);
            long lineNumber = 0;

            while (await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false) is { } line)
            {
                lineNumber++;
                if (tail.Count == maximumLines)
                {
                    tail.Dequeue();
                }

                tail.Enqueue(CreateLogLine(lineNumber, line));
            }

            return tail.ToArray();
        }
        catch (Exception exception) when (IsFileAccessException(exception))
        {
            return null;
        }
    }

    private static OllamaLogLine CreateLogLine(long lineNumber, string line)
    {
        if (line.Length > MaximumLineLength)
        {
            line = line[..MaximumLineLength] + " ... [TRUNCATED]";
        }

        var sanitized = RedactSecrets(line);
        var wasRedacted = !string.Equals(line, sanitized, StringComparison.Ordinal);
        return new OllamaLogLine(
            lineNumber,
            sanitized,
            wasRedacted || SuspiciousLinePattern().IsMatch(sanitized),
            wasRedacted);
    }

    private static string RedactSecrets(string line)
    {
        var sanitized = UriUserInfoPattern().Replace(line, "${scheme}" + RedactedValue + "@");
        sanitized = QuotedCredentialPattern().Replace(
            sanitized,
            "${prefix}${quote}" + RedactedValue + "${quote}");
        sanitized = AuthorizationHeaderPattern().Replace(sanitized, "${prefix}" + RedactedValue);
        sanitized = UnquotedCredentialPattern().Replace(sanitized, "${prefix}" + RedactedValue);
        sanitized = BearerTokenPattern().Replace(sanitized, "Bearer " + RedactedValue);
        return SecretFingerprintPattern().Replace(sanitized, RedactedValue);
    }

    private static IReadOnlyList<string> GetDefaultOfficialLogPaths()
    {
        if (OperatingSystem.IsWindows())
        {
            var localApplicationData = Environment.GetEnvironmentVariable("LOCALAPPDATA");
            if (string.IsNullOrWhiteSpace(localApplicationData))
            {
                localApplicationData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            }

            return string.IsNullOrWhiteSpace(localApplicationData)
                ? []
                : [Path.Combine(localApplicationData, "Ollama", "server.log")];
        }

        if (OperatingSystem.IsMacOS())
        {
            var userHome = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            return string.IsNullOrWhiteSpace(userHome)
                ? []
                : [Path.Combine(userHome, ".ollama", "logs", "server.log")];
        }

        return [];
    }

    private static string? TryGetFullPath(string path)
    {
        try
        {
            return Path.GetFullPath(path);
        }
        catch (Exception exception) when (IsFileAccessException(exception))
        {
            return null;
        }
    }

    private static StringComparer GetPathComparer() =>
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    private static bool IsFileAccessException(Exception exception) =>
        exception is ArgumentException
            or IOException
            or NotSupportedException
            or SecurityException
            or UnauthorizedAccessException;

    [GeneratedRegex(
        """(?<scheme>\b[a-z][a-z0-9+.-]*://)[^/\s:@]+:[^/\s@]+@""",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex UriUserInfoPattern();

    [GeneratedRegex(
        """(?<prefix>\b(?:[a-z0-9]+[_-])*(?:api[_-]?key|access[_-]?token|refresh[_-]?token|token|secret|client[_-]?secret|password|passwd|credential|authorization)\b\s*["']?\s*[:=]\s*)(?<quote>["'])(?:\\.|(?!\k<quote>).)*\k<quote>""",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex QuotedCredentialPattern();

    [GeneratedRegex(
        """(?<prefix>\b(?:proxy[_-])?authorization\b\s*["']?\s*[:=]\s*)(?:[a-z][a-z0-9_-]*\s+)?[^\s,;"'}\]&]+""",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex AuthorizationHeaderPattern();

    [GeneratedRegex(
        """(?<prefix>\b(?:[a-z0-9]+[_-])*(?:api[_-]?key|access[_-]?token|refresh[_-]?token|token|secret|client[_-]?secret|password|passwd|credential|authorization)\b\s*["']?\s*[:=]\s*)(?:Bearer\s+)?[^\s,;"'}\]&]+""",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex UnquotedCredentialPattern();

    [GeneratedRegex(
        """\bBearer\s+[A-Za-z0-9._~+/=-]+""",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex BearerTokenPattern();

    [GeneratedRegex(
        """\b(?:sk-[A-Za-z0-9_-]{12,}|gh[pousr]_[A-Za-z0-9]{20,}|xox[baprs]-[A-Za-z0-9-]{10,}|AKIA[0-9A-Z]{16}|eyJ[A-Za-z0-9_-]+\.[A-Za-z0-9_-]+\.[A-Za-z0-9_-]+)\b""",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SecretFingerprintPattern();

    [GeneratedRegex(
        """\b(?:warn(?:ing)?|error|fatal|panic|exception|critical|failed|failure|unauthori[sz]ed|forbidden)\b""",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SuspiciousLinePattern();

    private sealed record LogCandidate(OllamaLogSource Source, string Path);
}
