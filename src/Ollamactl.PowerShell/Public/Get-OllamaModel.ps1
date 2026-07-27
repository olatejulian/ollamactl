function Get-OllamaModel {
  <#
  .SYNOPSIS
  Gets installed or currently running Ollama models.

  .DESCRIPTION
  Calls either `ollamactl model list --output json` or
  `ollamactl model running --output json` and converts the response into
  PowerShell objects.

  .PARAMETER Running
  Returns only models currently loaded by the Ollama server.

  .PARAMETER OllamaHost
  Overrides the Ollama host as host:port or an HTTP(S) URL.

  .PARAMETER TimeoutSeconds
  Specifies the HTTP timeout from 1 through 600 seconds.

  .PARAMETER ConfigDirectory
  Overrides the ollamactl configuration directory.

  .PARAMETER ExecutablePath
  Specifies a literal path to ollamactl.exe. When omitted, OLLAMACTL_EXE and
  then PATH are searched.

  .EXAMPLE
  Get-OllamaModel

  Gets all installed models.

  .EXAMPLE
  Get-OllamaModel -Running

  Gets models currently loaded in memory.

  .OUTPUTS
  System.Management.Automation.PSObject

  .NOTES
  The command can return zero, one, or many model objects.

  .LINK
  https://github.com/olatejulian/ollamactl
  #>
  [CmdletBinding()]
  [OutputType([psobject])]
  param(
    [switch]$Running,

    [string]$OllamaHost,

    [ValidateRange(1, 600)]
    [int]$TimeoutSeconds,

    [string]$ConfigDirectory,

    [string]$ExecutablePath
  )

  $Subcommand = if ($Running) { 'running' } else { 'list' }
  $Parameters = @{ Command = @('model', $Subcommand) }
  foreach ($Name in 'OllamaHost', 'TimeoutSeconds', 'ConfigDirectory') {
    if ($PSBoundParameters.ContainsKey($Name)) {
      $Parameters[$Name] = $PSBoundParameters[$Name]
    }
  }

  $Arguments = @(ConvertTo-OllamactlArgumentList @Parameters)
  return Invoke-OllamactlJson `
    -ArgumentList $Arguments `
    -ExecutablePath $ExecutablePath
}
