using System.Text.Json;

namespace Ollamactl.Infrastructure.Processes;

internal static class ServerStateStore
{
    private const int ReplaceAttempts = 40;
    private static readonly TimeSpan ReplaceRetryDelay = TimeSpan.FromMilliseconds(25);

    public static async Task<ServerProcessState?> ReadAsync(
        string stateFile,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(stateFile))
        {
            return null;
        }

        try
        {
            await using var stream = new FileStream(
                stateFile,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete,
                bufferSize: 4096,
                useAsync: true);
            return await JsonSerializer.DeserializeAsync(
                    stream,
                    ProcessJsonContext.Default.ServerProcessState,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public static async Task WriteAsync(
        string stateFile,
        ServerProcessState state,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(state);
        var directory = Path.GetDirectoryName(stateFile)
            ?? throw new ArgumentException("The state file must have a parent directory.", nameof(stateFile));
        Directory.CreateDirectory(directory);
        var temporaryFile = Path.Combine(directory, $".{Path.GetFileName(stateFile)}.{Guid.NewGuid():N}.tmp");

        try
        {
            await using (var stream = new FileStream(
                temporaryFile,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 4096,
                useAsync: true))
            {
                await JsonSerializer.SerializeAsync(
                        stream,
                        state,
                        ProcessJsonContext.Default.ServerProcessState,
                        cancellationToken)
                    .ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            }

            await ReplaceAsync(temporaryFile, stateFile, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            if (File.Exists(temporaryFile))
            {
                try
                {
                    File.Delete(temporaryFile);
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                }
            }
        }
    }

    public static void Delete(string stateFile)
    {
        if (File.Exists(stateFile))
        {
            File.Delete(stateFile);
        }
    }

    private static async Task ReplaceAsync(
        string temporaryFile,
        string stateFile,
        CancellationToken cancellationToken)
    {
        for (var attempt = 1; ; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                File.Move(temporaryFile, stateFile, overwrite: true);
                return;
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException
                && attempt < ReplaceAttempts)
            {
                await Task.Delay(ReplaceRetryDelay, cancellationToken).ConfigureAwait(false);
            }
        }
    }
}
