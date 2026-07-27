@{
  RootModule = 'Ollamactl.PowerShell.psm1'
  ModuleVersion = '0.3.0'
  GUID = '9b0b2a7a-3a6b-4d8e-9b6c-7f5c2b1d4e0a'
  Author = 'Julian'
  Description = 'PowerShell commands for the ollamactl executable, including structured Ollama health, model, process, and server management.'
  PowerShellVersion = '7.2'
  CompatiblePSEditions = @('Core')
  FunctionsToExport = @(
    'Get-OllamaEndpoint'
    'Get-OllamaHealth'
    'Get-OllamaModel'
    'Get-OllamaProcess'
    'Invoke-Ollamactl'
    'Start-OllamaServer'
    'Stop-OllamaServer'
  )
  CmdletsToExport = @()
  VariablesToExport = @()
  AliasesToExport = @()
  PrivateData = @{
    PSData = @{
      Tags = @('Ollama', 'CLI', 'PowerShell', 'LLM', 'AI')
      ProjectUri = 'https://github.com/olatejulian/ollamactl'
      ReleaseNotes = 'Adds a thin, structured PowerShell wrapper over ollamactl.exe.'
    }
  }
}
