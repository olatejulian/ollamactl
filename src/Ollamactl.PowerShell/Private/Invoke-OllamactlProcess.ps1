function Invoke-OllamactlProcess {
  <#
  .SYNOPSIS
  Executes ollamactl without invoking a command shell.

  .DESCRIPTION
  Starts the resolved ollamactl executable with ProcessStartInfo.ArgumentList,
  captures stdout and stderr independently, and normally converts a nonzero
  exit code into a terminating PowerShell error.

  .PARAMETER ArgumentList
  Specifies the exact arguments to pass to ollamactl.

  .PARAMETER ExecutablePath
  Specifies an optional literal path to ollamactl.exe.

  .PARAMETER AllowNonZeroExitCode
  Returns the captured result even when ollamactl reports a nonzero exit code.

  .EXAMPLE
  Invoke-OllamactlProcess -ArgumentList @('--version')

  Executes the version command and returns its captured process result.

  .OUTPUTS
  System.Management.Automation.PSCustomObject

  .NOTES
  This is an internal module function.
  #>
  [CmdletBinding()]
  [OutputType([pscustomobject])]
  param(
    [AllowEmptyCollection()]
    [string[]]$ArgumentList = @(),

    [string]$ExecutablePath,

    [switch]$AllowNonZeroExitCode
  )

  $ResolvedExecutable = Resolve-OllamactlExecutable -ExecutablePath $ExecutablePath
  $StartInfo = [System.Diagnostics.ProcessStartInfo]::new()
  $StartInfo.FileName = $ResolvedExecutable
  $StartInfo.UseShellExecute = $false
  $StartInfo.CreateNoWindow = $true
  $StartInfo.RedirectStandardOutput = $true
  $StartInfo.RedirectStandardError = $true
  $StartInfo.StandardOutputEncoding = [System.Text.Encoding]::UTF8
  $StartInfo.StandardErrorEncoding = [System.Text.Encoding]::UTF8

  foreach ($Argument in $ArgumentList) {
    $StartInfo.ArgumentList.Add($Argument)
  }

  $Process = [System.Diagnostics.Process]::new()
  $Process.StartInfo = $StartInfo
  try {
    if (-not $Process.Start()) {
      throw [System.InvalidOperationException]::new('The ollamactl process could not be started.')
    }

    $StandardOutputTask = $Process.StandardOutput.ReadToEndAsync()
    $StandardErrorTask = $Process.StandardError.ReadToEndAsync()
    $Process.WaitForExit()
    $StandardOutput = $StandardOutputTask.GetAwaiter().GetResult()
    $StandardError = $StandardErrorTask.GetAwaiter().GetResult()
    $ExitCode = $Process.ExitCode
  }
  catch {
    $Exception = [System.InvalidOperationException]::new(
      "Failed to execute ollamactl at '$ResolvedExecutable'.",
      $_.Exception
    )
    $ErrorRecord = [System.Management.Automation.ErrorRecord]::new(
      $Exception,
      'Ollamactl.ProcessStartFailed',
      [System.Management.Automation.ErrorCategory]::OpenError,
      $ResolvedExecutable
    )
    $PSCmdlet.ThrowTerminatingError($ErrorRecord)
  }
  finally {
    $Process.Dispose()
  }

  if ($ExitCode -ne 0 -and -not $AllowNonZeroExitCode) {
    $Message = "ollamactl exited with code $ExitCode."
    if (-not [string]::IsNullOrWhiteSpace($StandardError)) {
      $Message = "$Message $($StandardError.Trim())"
    }

    $Exception = [System.InvalidOperationException]::new($Message)
    $ErrorRecord = [System.Management.Automation.ErrorRecord]::new(
      $Exception,
      'Ollamactl.NativeCommandFailed',
      [System.Management.Automation.ErrorCategory]::InvalidResult,
      $ResolvedExecutable
    )
    $PSCmdlet.ThrowTerminatingError($ErrorRecord)
  }

  $Result = [pscustomobject]@{
    ExecutablePath = $ResolvedExecutable
    Arguments = @($ArgumentList)
    ExitCode = $ExitCode
    StandardOutput = $StandardOutput
    StandardError = $StandardError
  }
  $Result.PSObject.TypeNames.Insert(0, 'Ollamactl.ProcessResult')
  return $Result
}
