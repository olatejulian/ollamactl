function Start-OllamaServer {
  <#
  .SYNOPSIS
  Starts an ollamactl-managed Ollama server in the background.

  .DESCRIPTION
  Calls `ollamactl server start --output json`. ollamactl loads the selected
  environment file into the child server, starts Ollama in the background,
  records process ownership, and redirects supported server logs.

  The command supports PowerShell -WhatIf and -Confirm. Paths supplied for the
  wrapper executable, Ollama executable, and environment file are resolved
  literally without wildcard expansion.

  .PARAMETER OllamaPath
  Specifies a literal path to the native ollama executable. Otherwise,
  OLLAMA_EXE and PATH are handled by ollamactl.

  .PARAMETER EnvironmentFile
  Specifies a literal path to the dotenv file loaded into the server process.

  .PARAMETER ConfigDirectory
  Overrides the ollamactl configuration directory.

  .PARAMETER ExecutablePath
  Specifies a literal path to ollamactl.exe. When omitted, OLLAMACTL_EXE and
  then PATH are searched.

  .EXAMPLE
  Start-OllamaServer

  Starts Ollama using the default paths and environment file.

  .EXAMPLE
  Start-OllamaServer -EnvironmentFile '.\server.env' -WhatIf

  Describes the start operation without starting a process.

  .OUTPUTS
  System.Management.Automation.PSObject

  .NOTES
  Only the process identity recorded by ollamactl can later be stopped as an
  owned server.

  .LINK
  https://github.com/olatejulian/ollamactl
  #>
  [CmdletBinding(SupportsShouldProcess, ConfirmImpact = 'Medium')]
  [OutputType([psobject])]
  param(
    [string]$OllamaPath,

    [string]$EnvironmentFile,

    [string]$ConfigDirectory,

    [string]$ExecutablePath
  )

  $Target = if ([string]::IsNullOrWhiteSpace($ConfigDirectory)) {
    'default ollamactl server'
  }
  else {
    "ollamactl server in '$ConfigDirectory'"
  }

  if (-not $PSCmdlet.ShouldProcess($Target, 'Start Ollama in the background')) {
    return
  }

  $Parameters = @{ Command = @('server', 'start') }
  if ($PSBoundParameters.ContainsKey('ConfigDirectory')) {
    $Parameters.ConfigDirectory = $ConfigDirectory
  }

  $Arguments = [System.Collections.Generic.List[string]]::new()
  foreach ($Argument in @(ConvertTo-OllamactlArgumentList @Parameters)) {
    $Arguments.Add($Argument)
  }

  if ($PSBoundParameters.ContainsKey('OllamaPath')) {
    $ResolvedOllamaPath = Resolve-OllamactlFile `
      -LiteralPath $OllamaPath `
      -Purpose 'Ollama executable'
    $Arguments.Add('--ollama-path')
    $Arguments.Add($ResolvedOllamaPath)
  }

  if ($PSBoundParameters.ContainsKey('EnvironmentFile')) {
    $ResolvedEnvironmentFile = Resolve-OllamactlFile `
      -LiteralPath $EnvironmentFile `
      -Purpose 'Ollama environment file'
    $Arguments.Add('--env-file')
    $Arguments.Add($ResolvedEnvironmentFile)
  }

  return Invoke-OllamactlJson `
    -ArgumentList $Arguments.ToArray() `
    -ExecutablePath $ExecutablePath
}
