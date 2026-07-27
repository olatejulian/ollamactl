function Get-OllamaProcess {
  <#
  .SYNOPSIS
  Gets Ollama-related process information.

  .DESCRIPTION
  Calls `ollamactl process list --output json` and returns structured process
  identity, ownership, resource, executable, and server-state information.

  .PARAMETER All
  Includes Ollama processes not owned by ollamactl as well as the managed
  server process tree.

  .PARAMETER ConfigDirectory
  Overrides the ollamactl configuration directory used to identify ownership.

  .PARAMETER ExecutablePath
  Specifies a literal path to ollamactl.exe. When omitted, OLLAMACTL_EXE and
  then PATH are searched.

  .EXAMPLE
  Get-OllamaProcess

  Gets the ollamactl-managed Ollama process information.

  .EXAMPLE
  Get-OllamaProcess -All

  Includes every discoverable Ollama-related process.

  .OUTPUTS
  System.Management.Automation.PSObject

  .NOTES
  Process ownership validation is performed by ollamactl.exe.

  .LINK
  https://github.com/olatejulian/ollamactl
  #>
  [CmdletBinding()]
  [OutputType([psobject])]
  param(
    [switch]$All,

    [string]$ConfigDirectory,

    [string]$ExecutablePath
  )

  $Parameters = @{ Command = @('process', 'list') }
  if ($PSBoundParameters.ContainsKey('ConfigDirectory')) {
    $Parameters.ConfigDirectory = $ConfigDirectory
  }

  $Arguments = [System.Collections.Generic.List[string]]::new()
  foreach ($Argument in @(ConvertTo-OllamactlArgumentList @Parameters)) {
    $Arguments.Add($Argument)
  }

  if ($All) {
    $Arguments.Add('--all')
  }

  return Invoke-OllamactlJson `
    -ArgumentList $Arguments.ToArray() `
    -ExecutablePath $ExecutablePath
}
