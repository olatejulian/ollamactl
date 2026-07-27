namespace Ollamactl.EndToEndTests;

internal sealed class TemporaryTestDirectory : IAsyncDisposable
{
    private const string DirectoryPrefix = "ollamactl-e2e-fixture-";
    private static readonly TimeSpan RetryDelay = TimeSpan.FromMilliseconds(100);
    private const int CleanupAttempts = 10;
    private int disposed;

    public TemporaryTestDirectory()
    {
        var temporaryRoot = System.IO.Path.GetFullPath(System.IO.Path.GetTempPath());
        Path = System.IO.Path.Combine(temporaryRoot, $"{DirectoryPrefix}{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public string CreateDirectory(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        var candidate = System.IO.Path.GetFullPath(System.IO.Path.Combine(Path, name));
        EnsureContained(candidate);
        Directory.CreateDirectory(candidate);
        return candidate;
    }

    public string GetPath(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        var candidate = System.IO.Path.GetFullPath(System.IO.Path.Combine(Path, name));
        EnsureContained(candidate);
        return candidate;
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0)
        {
            return;
        }

        var fullPath = System.IO.Path.GetFullPath(Path);
        var directoryName = System.IO.Path.GetFileName(fullPath);
        if (!directoryName.StartsWith(DirectoryPrefix, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Refusing to clean an unexpected test path: '{fullPath}'.");
        }

        for (var attempt = 1; attempt <= CleanupAttempts; attempt++)
        {
            try
            {
                Directory.Delete(fullPath, recursive: true);
                return;
            }
            catch (DirectoryNotFoundException)
            {
                return;
            }
            catch (IOException) when (attempt < CleanupAttempts)
            {
                await Task.Delay(RetryDelay).ConfigureAwait(false);
            }
            catch (UnauthorizedAccessException) when (attempt < CleanupAttempts)
            {
                await Task.Delay(RetryDelay).ConfigureAwait(false);
            }
        }
    }

    private void EnsureContained(string candidate)
    {
        var root = System.IO.Path.GetFullPath(Path)
            .TrimEnd(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar)
            + System.IO.Path.DirectorySeparatorChar;
        if (!candidate.StartsWith(
                root,
                OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"The test path escapes its sandbox: '{candidate}'.");
        }
    }
}
