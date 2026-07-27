function Invoke-OllamactlJson {
  <#
  .SYNOPSIS
  Executes ollamactl and parses its JSON output.

  .DESCRIPTION
  Calls the internal process runner and converts a successful, nonempty JSON
  document into PowerShell objects. Empty or invalid output is a terminating
  error.

  .PARAMETER ArgumentList
  Specifies the exact ollamactl arguments, including --output json.

  .PARAMETER ExecutablePath
  Specifies an optional literal path to ollamactl.exe.

  .PARAMETER AllowNonZeroExitCode
  Parses structured output even when the command uses a nonzero status code.

  .EXAMPLE
  Invoke-OllamactlJson -ArgumentList @('--output', 'json', 'endpoint')

  Returns the endpoint response as a PowerShell object.

  .OUTPUTS
  System.Management.Automation.PSObject

  .NOTES
  This is an internal module function.
  #>
  [CmdletBinding()]
  [OutputType([psobject])]
  param(
    [Parameter(Mandatory)]
    [ValidateNotNull()]
    [string[]]$ArgumentList,

    [string]$ExecutablePath,

    [switch]$AllowNonZeroExitCode
  )

  $Result = Invoke-OllamactlProcess `
    -ArgumentList $ArgumentList `
    -ExecutablePath $ExecutablePath `
    -AllowNonZeroExitCode:$AllowNonZeroExitCode

  if ([string]::IsNullOrWhiteSpace($Result.StandardOutput)) {
    $Exception = [System.IO.InvalidDataException]::new(
      'ollamactl returned an empty response where JSON was expected.'
    )
    $ErrorRecord = [System.Management.Automation.ErrorRecord]::new(
      $Exception,
      'Ollamactl.EmptyJson',
      [System.Management.Automation.ErrorCategory]::InvalidData,
      $Result
    )
    $PSCmdlet.ThrowTerminatingError($ErrorRecord)
  }

  try {
    return $Result.StandardOutput |
      ConvertFrom-Json -Depth 100 -ErrorAction Stop
  }
  catch {
    $Exception = [System.IO.InvalidDataException]::new(
      'ollamactl returned invalid JSON.',
      $_.Exception
    )
    $ErrorRecord = [System.Management.Automation.ErrorRecord]::new(
      $Exception,
      'Ollamactl.InvalidJson',
      [System.Management.Automation.ErrorCategory]::InvalidData,
      $Result.StandardOutput
    )
    $PSCmdlet.ThrowTerminatingError($ErrorRecord)
  }
}
