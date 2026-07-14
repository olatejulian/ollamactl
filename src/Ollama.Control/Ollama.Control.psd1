@{

    RootModule        = "Ollama.Tools.psm1"
    ModuleVersion     = "0.1.0"
    GUID              = "9b0b2a7a-3a6b-4d8e-9b6c-7f5c2b1d4e0a"
    Author            = "Julian"
    Description       = "PowerShell module + CLI for managing Ollama and integrating it with VS Code AI agent extensions."
    PowerShellVersion = "7.0"
    FunctionsToExport = @(
        "Start-Ollama",
        "Stop-Ollama",
        "Get-OllamaStatus",
        "Start-OllamaModel",
        "Get-OllamaConfig",
        "Get-OllamaPaths",
        "Import-DotEnv",
        "Test-OllamaRunning",
        "Write-Log",
        "Get-OllamaEndpoint",
        "Test-OllamaToolCalling",
        "Export-OllamaConfigForExtension",
        "Show-OllamaIntegrationGuide"
    )
    CmdletsToExport   = @()
    VariablesToExport = @()
    AliasesToExport   = @()
    PrivateData       = @{
        PSData = @{
            ProjectUri = ""
        }
    }
}