function Resolve-OllamactlExecutable {
  <#
  .SYNOPSIS
  Locates the ollamactl executable.

  .DESCRIPTION
  Resolves ollamactl using an explicit path, OLLAMACTL_EXE, or an application
  found on PATH, in that order. Alias, function, and script command types are
  excluded from PATH discovery.

  .PARAMETER ExecutablePath
  Specifies an explicit literal path to ollamactl.exe.

  .EXAMPLE
  Resolve-OllamactlExecutable -ExecutablePath 'C:\Tools\ollamactl.exe'

  Resolves the explicitly supplied executable.

  .OUTPUTS
  System.String

  .NOTES
  This is an internal module function.
  #>
  [CmdletBinding()]
  [OutputType([string])]
  param(
    [string]$ExecutablePath
  )

  if (-not [string]::IsNullOrWhiteSpace($ExecutablePath)) {
    return Resolve-OllamactlFile `
      -LiteralPath $ExecutablePath `
      -Purpose 'ollamactl executable'
  }

  if (-not [string]::IsNullOrWhiteSpace($env:OLLAMACTL_EXE)) {
    return Resolve-OllamactlFile `
      -LiteralPath $env:OLLAMACTL_EXE `
      -Purpose 'OLLAMACTL_EXE executable'
  }

  $Commands = @(
    Get-Command `
      -Name 'ollamactl.exe', 'ollamactl' `
      -CommandType Application `
      -ErrorAction SilentlyContinue
  )

  foreach ($Command in $Commands) {
    $CommandPath = if ($Command.Path) { $Command.Path } else { $Command.Source }
    if (-not [string]::IsNullOrWhiteSpace($CommandPath)) {
      return Resolve-OllamactlFile `
        -LiteralPath $CommandPath `
        -Purpose 'ollamactl executable from PATH'
    }
  }

  $Exception = [System.IO.FileNotFoundException]::new(
    'Could not find ollamactl. Use -ExecutablePath, set OLLAMACTL_EXE, or add ollamactl to PATH.'
  )
  $ErrorRecord = [System.Management.Automation.ErrorRecord]::new(
    $Exception,
    'Ollamactl.ExecutableNotFound',
    [System.Management.Automation.ErrorCategory]::ObjectNotFound,
    'ollamactl'
  )
  $PSCmdlet.ThrowTerminatingError($ErrorRecord)
}
