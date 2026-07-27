using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Ollamactl.Application.Diagnostics;
using Ollamactl.Application.Logging;
using Ollamactl.Application.Processes;
using Ollamactl.Application.Server;
using Ollamactl.Domain.Models;

namespace Ollamactl.Cli.Runtime;

public sealed class CliOutput(ICliConsole console)
{
    public void WriteStatus(OllamaStatus status, OutputFormat format)
    {
        if (format == OutputFormat.Json)
        {
            WriteJson(status, CliJsonContext.Default.OllamaStatus);
            return;
        }

        console.StandardOutput.WriteLine($"Ollama {status.Version} is healthy at {status.Endpoint}");
        console.StandardOutput.WriteLine(
            $"Installed models: {status.Models.Count}; running models: {status.RunningModels.Count}");
        if (status.Models.Count > 0)
        {
            console.StandardOutput.WriteLine();
            WriteModelTable(status.Models);
        }
    }

    public void WriteModels(OllamaModel[] models, OutputFormat format)
    {
        if (format == OutputFormat.Json)
        {
            WriteJson(models, CliJsonContext.Default.OllamaModelArray);
            return;
        }

        if (models.Length == 0)
        {
            console.StandardOutput.WriteLine("No models installed.");
            return;
        }

        WriteModelTable(models);
    }

    public void WriteRunningModels(RunningOllamaModel[] models, OutputFormat format)
    {
        if (format == OutputFormat.Json)
        {
            WriteJson(models, CliJsonContext.Default.RunningOllamaModelArray);
            return;
        }

        if (models.Length == 0)
        {
            console.StandardOutput.WriteLine("No models are currently loaded.");
            return;
        }

        console.StandardOutput.WriteLine($"{"NAME",-36} {"SIZE",10} {"VRAM",10} EXPIRES");
        foreach (var model in models)
        {
            console.StandardOutput.WriteLine(
                $"{Truncate(model.Name, 36),-36} {FormatBytes(model.SizeBytes),10} "
                + $"{FormatBytes(model.SizeVramBytes),10} "
                + $"{model.ExpiresAt?.ToString("u", CultureInfo.InvariantCulture) ?? "-"}");
        }
    }

    public void WriteChat(ChatResult result, OutputFormat format)
    {
        if (format == OutputFormat.Json)
        {
            WriteJson(result, CliJsonContext.Default.ChatResult);
            return;
        }

        console.StandardOutput.WriteLine(result.Content);
    }

    public void WriteToolProbe(ToolProbeResult result, OutputFormat format)
    {
        if (format == OutputFormat.Json)
        {
            WriteJson(result, CliJsonContext.Default.ToolProbeResult);
            return;
        }

        console.StandardOutput.WriteLine(result.CalledTool
            ? $"Model {result.Model} called {result.ToolName} in {result.ElapsedMilliseconds} ms."
            : $"Model {result.Model} did not call the test tool ({result.ElapsedMilliseconds} ms).");
        if (result.ArgumentsJson is not null)
        {
            console.StandardOutput.WriteLine($"Arguments: {result.ArgumentsJson}");
        }
    }

    public void WriteServerStart(ServerStartResult result, OutputFormat format)
    {
        if (format == OutputFormat.Json)
        {
            WriteJson(result, CliJsonContext.Default.ServerStartResult);
            return;
        }

        console.StandardOutput.WriteLine(result.Started
            ? $"Ollama server started (supervisor PID {result.SupervisorProcessId}, Ollama PID {result.ProcessId})."
            : $"An ollamactl-managed server is already running (Ollama PID {result.ProcessId}).");
        WriteLogLocations(result.StandardOutputLog, result.StandardErrorLog);
    }

    public void WriteServerLaunch(ServerLaunchResult result, OutputFormat format)
    {
        if (format == OutputFormat.Json)
        {
            WriteJson(result, CliJsonContext.Default.ServerLaunchResult);
            return;
        }

        if (result.Process is null)
        {
            console.StandardOutput.WriteLine(
                $"Ollama API {result.ApiVersion} was already available; no process was started.");
            return;
        }

        WriteServerStart(result.Process, OutputFormat.Text);
        console.StandardOutput.WriteLine(
            $"API ready: version {result.ApiVersion} after {result.ReadinessMilliseconds} ms.");
    }

    public void WriteServerStop(ServerStopResult result, OutputFormat format)
    {
        if (format == OutputFormat.Json)
        {
            WriteJson(result, CliJsonContext.Default.ServerStopResult);
            return;
        }

        console.StandardOutput.WriteLine(result.Message);
    }

    public void WriteServerRestart(ServerRestartView result, OutputFormat format)
    {
        if (format == OutputFormat.Json)
        {
            WriteJson(result, CliJsonContext.Default.ServerRestartView);
            return;
        }

        console.StandardOutput.WriteLine(result.Stop.Message);
        WriteServerLaunch(result.Start, OutputFormat.Text);
    }

    public void WriteManagedServerStatus(ManagedServerStatus status, OutputFormat format)
    {
        if (format == OutputFormat.Json)
        {
            WriteJson(status, CliJsonContext.Default.ManagedServerStatus);
            return;
        }

        console.StandardOutput.WriteLine($"State:          {status.State}");
        console.StandardOutput.WriteLine($"Supervisor PID: {status.SupervisorProcessId?.ToString(CultureInfo.InvariantCulture) ?? "-"}");
        console.StandardOutput.WriteLine($"Ollama PID:     {status.ProcessId?.ToString(CultureInfo.InvariantCulture) ?? "-"}");
        console.StandardOutput.WriteLine($"Executable:     {status.Executable ?? "-"}");
        console.StandardOutput.WriteLine($"State file:     {status.StateFile}");
        WriteLogLocations(status.StandardOutputLog, status.StandardErrorLog);
    }

    public void WriteProcesses(IReadOnlyList<OllamaProcessInfo> processes, OutputFormat format)
    {
        if (format == OutputFormat.Json)
        {
            WriteJson(processes.ToArray(), CliJsonContext.Default.OllamaProcessInfoArray);
            return;
        }

        if (processes.Count == 0)
        {
            console.StandardOutput.WriteLine("No matching Ollama processes found.");
            return;
        }

        console.StandardOutput.WriteLine($"{"PID",8} {"NAME",24} {"MEMORY",12} STARTED (UTC)");
        foreach (var process in processes)
        {
            console.StandardOutput.WriteLine(
                $"{process.ProcessId,8} {Truncate(process.Name, 24),24} "
                + $"{(process.WorkingSetBytes is { } bytes ? FormatBytes(bytes) : "-"),12} "
                + $"{process.StartedAtUtc?.ToString("u", CultureInfo.InvariantCulture) ?? "-"}");
        }
    }

    public void WriteLogs(IReadOnlyList<OllamaLogTail> logs, OutputFormat format)
    {
        if (format == OutputFormat.Json)
        {
            WriteJson(logs.ToArray(), CliJsonContext.Default.OllamaLogTailArray);
            return;
        }

        if (logs.Count == 0)
        {
            console.StandardOutput.WriteLine("No readable Ollama logs found.");
            return;
        }

        foreach (var log in logs)
        {
            console.StandardOutput.WriteLine($"==> {log.Source}: {log.Path} <==");
            foreach (var line in log.Lines)
            {
                var marker = line.IsSuspicious ? "!" : " ";
                console.StandardOutput.WriteLine($"{marker} {line.LineNumber,6}: {line.Text}");
            }

            console.StandardOutput.WriteLine();
        }
    }

    public void WriteHealth(OllamaHealthReport report, OutputFormat format)
    {
        if (format == OutputFormat.Json)
        {
            WriteJson(report, CliJsonContext.Default.OllamaHealthReport);
            return;
        }

        console.StandardOutput.WriteLine(
            $"Ollama health: {report.Status.ToString().ToUpperInvariant()} at {report.Endpoint}");
        foreach (var check in report.Checks)
        {
            console.StandardOutput.WriteLine($"[{GetCheckLabel(check.Status),4}] {check.Summary}");
            if (!string.IsNullOrWhiteSpace(check.Details))
            {
                console.StandardOutput.WriteLine($"       {check.Details}");
            }

            if (!string.IsNullOrWhiteSpace(check.SuggestedAction))
            {
                console.StandardOutput.WriteLine($"       Action: {check.SuggestedAction}");
            }
        }

        console.StandardOutput.WriteLine($"Completed in {report.DurationMilliseconds} ms.");
    }

    public void WriteConfiguration(ConfigurationView configuration, OutputFormat format)
    {
        if (format == OutputFormat.Json)
        {
            WriteJson(configuration, CliJsonContext.Default.ConfigurationView);
            return;
        }

        console.StandardOutput.WriteLine($"Host:             {configuration.Host} ({configuration.HostSource})");
        console.StandardOutput.WriteLine($"Timeout:          {configuration.TimeoutSeconds}s ({configuration.TimeoutSource})");
        console.StandardOutput.WriteLine($"Config directory: {configuration.ConfigDirectory}");
        console.StandardOutput.WriteLine($"Environment file: {configuration.EnvFile}");
        console.StandardOutput.WriteLine($"Server state:     {configuration.ServerStateFile}");
        console.StandardOutput.WriteLine($"Ollama executable:{(configuration.OllamaExecutable is null ? " not found" : " " + configuration.OllamaExecutable)}");
    }

    public void WriteEnvironmentOrigins(EnvironmentOriginView[] variables, OutputFormat format)
    {
        if (format == OutputFormat.Json)
        {
            WriteJson(variables, CliJsonContext.Default.EnvironmentOriginViewArray);
            return;
        }

        console.StandardOutput.WriteLine($"{"NAME",-40} SOURCE");
        foreach (var variable in variables)
        {
            console.StandardOutput.WriteLine($"{Truncate(variable.Name, 40),-40} {variable.Source}");
        }
    }

    public void WriteModelAction(string model, string status, string text, OutputFormat format)
    {
        if (format == OutputFormat.Json)
        {
            WriteJson(new ModelActionResult(model, status), CliJsonContext.Default.ModelActionResult);
            return;
        }

        console.StandardOutput.WriteLine(text);
    }

    public void WriteError(int exitCode, string message, OutputFormat format)
    {
        if (format == OutputFormat.Json)
        {
            console.StandardError.WriteLine(JsonSerializer.Serialize(
                new CliError(exitCode, message),
                CliJsonContext.Default.CliError));
            return;
        }

        console.StandardError.WriteLine($"error: {message}");
    }

    public static string FormatBytes(long bytes)
    {
        string[] suffixes = ["B", "KiB", "MiB", "GiB", "TiB"];
        var value = (double)Math.Max(bytes, 0);
        var suffix = 0;
        while (value >= 1024 && suffix < suffixes.Length - 1)
        {
            value /= 1024;
            suffix++;
        }

        return suffix == 0
            ? $"{value:0} {suffixes[suffix]}"
            : $"{value:0.##} {suffixes[suffix]}";
    }

    private void WriteModelTable(IReadOnlyList<OllamaModel> models)
    {
        console.StandardOutput.WriteLine($"{"NAME",-36} {"SIZE",10} {"PARAMETERS",12} {"QUANT",8}");
        foreach (var model in models)
        {
            console.StandardOutput.WriteLine(
                $"{Truncate(model.Name, 36),-36} {FormatBytes(model.SizeBytes),10} "
                + $"{model.ParameterSize ?? "-",12} {model.QuantizationLevel ?? "-",8}");
        }
    }

    private void WriteLogLocations(string standardOutputLog, string standardErrorLog)
    {
        console.StandardOutput.WriteLine($"stdout: {standardOutputLog}");
        console.StandardOutput.WriteLine($"stderr: {standardErrorLog}");
    }

    private void WriteJson<T>(T value, JsonTypeInfo<T> typeInfo) =>
        console.StandardOutput.WriteLine(JsonSerializer.Serialize(value, typeInfo));

    private static string GetCheckLabel(DiagnosticCheckStatus status) => status switch
    {
        DiagnosticCheckStatus.Passed => "PASS",
        DiagnosticCheckStatus.Warning => "WARN",
        DiagnosticCheckStatus.Failed => "FAIL",
        DiagnosticCheckStatus.NotApplicable => "N/A",
        _ => "????",
    };

    private static string Truncate(string value, int maximumLength) =>
        value.Length <= maximumLength
            ? value
            : string.Concat(value.AsSpan(0, maximumLength - 1), "…");
}
