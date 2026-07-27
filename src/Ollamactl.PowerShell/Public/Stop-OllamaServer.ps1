function Stop-OllamaServer {
  <#
  .SYNOPSIS
  Stops the Ollama server owned by ollamactl.

  .DESCRIPTION
  Calls `ollamactl server stop --output json`. ollamactl validates the recorded
  PID, process start time, and executable identity before stopping the managed
  process tree. Unrelated Ollama processes are not stopped.

  The command supports PowerShell -WhatIf and -Confirm.

  .PARAMETER ConfigDirectory
  Overrides the ollamactl configuration directory containing managed process
  state.

  .PARAMETER ExecutablePath
  Specifies a literal path to ollamactl.exe. When omitted, OLLAMACTL_EXE and
  then PATH are searched.

  .EXAMPLE
  Stop-OllamaServer

  Stops the Ollama server previously started by ollamactl.

  .EXAMPLE
  Stop-OllamaServer -ConfigDirectory 'C:\OllamaConfig' -WhatIf

  Describes which managed server would be stopped without changing state.

  .OUTPUTS
  System.Management.Automation.PSObject

  .NOTES
  Stale state is handled by ollamactl without killing an unverified process.

  .LINK
  https://github.com/olatejulian/ollamactl
  #>
  [CmdletBinding(SupportsShouldProcess, ConfirmImpact = 'Medium')]
  [OutputType([psobject])]
  param(
    [string]$ConfigDirectory,

    [string]$ExecutablePath
  )

  $Target = if ([string]::IsNullOrWhiteSpace($ConfigDirectory)) {
    'default ollamactl server'
  }
  else {
    "ollamactl server in '$ConfigDirectory'"
  }

  if (-not $PSCmdlet.ShouldProcess($Target, 'Stop the owned Ollama process tree')) {
    return
  }

  $Parameters = @{ Command = @('server', 'stop') }
  if ($PSBoundParameters.ContainsKey('ConfigDirectory')) {
    $Parameters.ConfigDirectory = $ConfigDirectory
  }

  $Arguments = @(ConvertTo-OllamactlArgumentList @Parameters)
  return Invoke-OllamactlJson `
    -ArgumentList $Arguments `
    -ExecutablePath $ExecutablePath
}
