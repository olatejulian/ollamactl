namespace Ollamactl.Infrastructure.Processes;

public sealed record ApplicationLaunchInfo(
    string FileName,
    IReadOnlyList<string> PrefixArguments,
    string IdentityExecutable)
{
    public static ApplicationLaunchInfo ForCurrentProcess(string? entryAssemblyPath = null)
    {
        var processPath = Environment.ProcessPath
            ?? throw new InvalidOperationException("Unable to determine the current process executable.");
        var processName = Path.GetFileNameWithoutExtension(processPath);

        if (string.Equals(processName, "dotnet", StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(entryAssemblyPath))
            {
                throw new InvalidOperationException(
                    "The entry assembly path is required when ollamactl is hosted by dotnet.");
            }

            return new ApplicationLaunchInfo(
                Path.GetFullPath(processPath),
                [Path.GetFullPath(entryAssemblyPath)],
                Path.GetFullPath(processPath));
        }

        var fullPath = Path.GetFullPath(processPath);
        return new ApplicationLaunchInfo(fullPath, [], fullPath);
    }
}
