using System.ComponentModel;
using System.Diagnostics;
using System.Security;
using Ollamactl.Application.Abstractions;
using Ollamactl.Application.Processes;

namespace Ollamactl.Infrastructure.Processes;

public sealed class OllamaProcessInspector : IOllamaProcessInspector
{
    public Task<IReadOnlyList<OllamaProcessInfo>> InspectAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        Process[] processes;
        try
        {
            processes = Process.GetProcesses();
        }
        catch (Exception exception) when (IsProcessAccessException(exception))
        {
            return Task.FromResult<IReadOnlyList<OllamaProcessInfo>>([]);
        }

        try
        {
            var result = new List<OllamaProcessInfo>();
            foreach (var process in processes)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var name = TryRead(() => process.ProcessName);
                if (string.IsNullOrWhiteSpace(name)
                    || !name.StartsWith("ollama", StringComparison.OrdinalIgnoreCase)
                    || name.StartsWith("ollamactl", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var processId = TryRead(() => process.Id);
                if (processId is null)
                {
                    continue;
                }

                result.Add(new OllamaProcessInfo(
                    processId.Value,
                    name,
                    TryRead(() => process.MainModule?.FileName),
                    TryRead(() => new DateTimeOffset(process.StartTime).ToUniversalTime()),
                    TryRead(() => process.TotalProcessorTime),
                    TryRead(() => process.WorkingSet64)));
            }

            return Task.FromResult<IReadOnlyList<OllamaProcessInfo>>(
                result.OrderBy(item => item.ProcessId).ToArray());
        }
        finally
        {
            foreach (var process in processes)
            {
                process.Dispose();
            }
        }
    }

    private static string? TryRead(Func<string?> reader)
    {
        try
        {
            return reader();
        }
        catch (Exception exception) when (IsProcessAccessException(exception))
        {
            return null;
        }
    }

    private static T? TryRead<T>(Func<T> reader)
        where T : struct
    {
        try
        {
            return reader();
        }
        catch (Exception exception) when (IsProcessAccessException(exception))
        {
            return null;
        }
    }

    private static bool IsProcessAccessException(Exception exception) =>
        exception is ArgumentException
            or InvalidOperationException
            or NotSupportedException
            or SecurityException
            or UnauthorizedAccessException
            or Win32Exception;
}
