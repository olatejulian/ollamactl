function Invoke-Ollamactl {
  <#
  .SYNOPSIS
  Invokes ollamactl with an arbitrary argument list.

  .DESCRIPTION
  Locates ollamactl using -ExecutablePath, OLLAMACTL_EXE, or PATH and invokes
  it without a command shell. Each supplied argument is passed separately to
  prevent command-string injection. A nonzero native exit code is a
  terminating PowerShell error.

  By default, successful stdout is written as text lines. Use -PassThru to
  receive the captured exit code, stdout, stderr, executable path, and exact
  argument list as an Ollamactl.ProcessResult object.

  .PARAMETER ArgumentList
  Specifies arguments to pass to ollamactl. Remaining positional arguments
  are collected into this parameter.

  .PARAMETER ExecutablePath
  Specifies a literal path to ollamactl.exe. When omitted, OLLAMACTL_EXE and
  then PATH are searched.

  .PARAMETER PassThru
  Returns the captured process result instead of writing stdout as text.

  .EXAMPLE
  Invoke-Ollamactl --version

  Prints the ollamactl version.

  .EXAMPLE
  Invoke-Ollamactl model list --output json -PassThru

  Invokes the model-list command and returns its captured process result.

  .OUTPUTS
  System.String
  Ollamactl.ProcessResult

  .NOTES
  This command intentionally does not invoke a shell or evaluate argument text.

  .LINK
  https://github.com/olatejulian/ollamactl
  #>
  [CmdletBinding()]
  [OutputType([string], [pscustomobject])]
  param(
    [Parameter(Position = 0, ValueFromRemainingArguments)]
    [AllowEmptyCollection()]
    [Alias('Arguments')]
    [string[]]$ArgumentList = @(),

    [string]$ExecutablePath,

    [switch]$PassThru
  )

  $Result = Invoke-OllamactlProcess `
    -ArgumentList $ArgumentList `
    -ExecutablePath $ExecutablePath

  if ($PassThru) {
    return $Result
  }

  if (-not [string]::IsNullOrWhiteSpace($Result.StandardError)) {
    Write-Verbose -Message $Result.StandardError.Trim()
  }

  if (-not [string]::IsNullOrEmpty($Result.StandardOutput)) {
    $Lines = $Result.StandardOutput -split '\r?\n'
    if ($Lines.Count -gt 0 -and $Lines[-1] -eq '') {
      $Lines = $Lines[0..($Lines.Count - 2)]
    }
    Write-Output -InputObject $Lines -NoEnumerate:$false
  }
}
