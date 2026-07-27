function Get-OllamaHealth {
  <#
  .SYNOPSIS
  Runs the complete Ollama health diagnosis.

  .DESCRIPTION
  Calls `ollamactl health --output json` and returns its structured diagnosis.
  The diagnosis can include REST API checks, native CLI checks, managed process
  state, model information, environment validation, and bounded log excerpts.

  .PARAMETER IncludeLogs
  Includes bounded Ollama stdout and stderr excerpts in the diagnosis.

  .PARAMETER LogTail
  Specifies how many lines to include from each log. This parameter requires
  -IncludeLogs.

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
  Get-OllamaHealth

  Runs the standard structured health diagnosis.

  .EXAMPLE
  Get-OllamaHealth -IncludeLogs -LogTail 200

  Runs the diagnosis and includes the last 200 lines of each supported log.

  .OUTPUTS
  System.Management.Automation.PSObject

  .NOTES
  Log output is supplied by ollamactl and should already be redacted.

  .LINK
  https://github.com/olatejulian/ollamactl
  #>
  [CmdletBinding()]
  [OutputType([psobject])]
  param(
    [switch]$IncludeLogs,

    [ValidateRange(1, 1000)]
    [int]$LogTail = 100,

    [string]$OllamaHost,

    [ValidateRange(1, 600)]
    [int]$TimeoutSeconds,

    [string]$ConfigDirectory,

    [string]$ExecutablePath
  )

  if ($PSBoundParameters.ContainsKey('LogTail') -and -not $IncludeLogs) {
    $Exception = [System.ArgumentException]::new('-LogTail requires -IncludeLogs.')
    $ErrorRecord = [System.Management.Automation.ErrorRecord]::new(
      $Exception,
      'Ollamactl.LogTailRequiresLogs',
      [System.Management.Automation.ErrorCategory]::InvalidArgument,
      $LogTail
    )
    $PSCmdlet.ThrowTerminatingError($ErrorRecord)
  }

  $Parameters = @{ Command = @('health') }
  foreach ($Name in 'OllamaHost', 'TimeoutSeconds', 'ConfigDirectory') {
    if ($PSBoundParameters.ContainsKey($Name)) {
      $Parameters[$Name] = $PSBoundParameters[$Name]
    }
  }

  $Arguments = [System.Collections.Generic.List[string]]::new()
  foreach ($Argument in @(ConvertTo-OllamactlArgumentList @Parameters)) {
    $Arguments.Add($Argument)
  }

  if ($IncludeLogs) {
    $Arguments.Add('--include-logs')
    $Arguments.Add('--log-tail')
    $Arguments.Add($LogTail.ToString([System.Globalization.CultureInfo]::InvariantCulture))
  }

  return Invoke-OllamactlJson `
    -ArgumentList $Arguments.ToArray() `
    -ExecutablePath $ExecutablePath `
    -AllowNonZeroExitCode
}
