function Resolve-OllamactlFile {
  <#
  .SYNOPSIS
  Resolves an existing file without wildcard expansion.

  .DESCRIPTION
  Resolves a filesystem path through Resolve-Path -LiteralPath and verifies
  that the result is a file. The function throws a terminating error for
  missing paths, directories, and non-filesystem providers.

  .PARAMETER LiteralPath
  Specifies the literal path to resolve.

  .PARAMETER Purpose
  Describes the file in any error message.

  .EXAMPLE
  Resolve-OllamactlFile -LiteralPath '.\ollamactl.exe' -Purpose 'ollamactl executable'

  Resolves a local executable path.

  .OUTPUTS
  System.String

  .NOTES
  This is an internal module function.
  #>
  [CmdletBinding()]
  [OutputType([string])]
  param(
    [Parameter(Mandatory)]
    [ValidateNotNullOrEmpty()]
    [string]$LiteralPath,

    [Parameter(Mandatory)]
    [ValidateNotNullOrEmpty()]
    [string]$Purpose
  )

  try {
    $ResolvedPath = Resolve-Path -LiteralPath $LiteralPath -ErrorAction Stop
  }
  catch {
    $Exception = [System.IO.FileNotFoundException]::new(
      "The $Purpose was not found: $LiteralPath",
      $LiteralPath,
      $_.Exception
    )
    $ErrorRecord = [System.Management.Automation.ErrorRecord]::new(
      $Exception,
      'Ollamactl.FileNotFound',
      [System.Management.Automation.ErrorCategory]::ObjectNotFound,
      $LiteralPath
    )
    $PSCmdlet.ThrowTerminatingError($ErrorRecord)
  }

  if ($ResolvedPath.Provider.Name -ne 'FileSystem' -or
    -not [System.IO.File]::Exists($ResolvedPath.ProviderPath)) {
    $Exception = [System.IO.FileNotFoundException]::new(
      "The $Purpose is not a filesystem file: $LiteralPath",
      $LiteralPath
    )
    $ErrorRecord = [System.Management.Automation.ErrorRecord]::new(
      $Exception,
      'Ollamactl.InvalidFile',
      [System.Management.Automation.ErrorCategory]::InvalidArgument,
      $LiteralPath
    )
    $PSCmdlet.ThrowTerminatingError($ErrorRecord)
  }

  return [System.IO.Path]::GetFullPath($ResolvedPath.ProviderPath)
}
