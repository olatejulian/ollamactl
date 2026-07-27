#Requires -Version 7.3

Set-StrictMode -Version Latest

function Get-ProcessEnvironmentSnapshot {
  [CmdletBinding()]
  param(
    [Parameter(Mandatory)]
    [string[]]$Name
  )

  $Snapshot = @{}
  foreach ($VariableName in $Name) {
    $Snapshot[$VariableName] = [Environment]::GetEnvironmentVariable(
      $VariableName,
      'Process'
    )
  }

  return $Snapshot
}

function Restore-ProcessEnvironment {
  [CmdletBinding()]
  param(
    [Parameter(Mandatory)]
    [hashtable]$Snapshot
  )

  foreach ($Entry in $Snapshot.GetEnumerator()) {
    [Environment]::SetEnvironmentVariable(
      [string]$Entry.Key,
      $Entry.Value,
      'Process'
    )
  }
}

function Get-DotnetTaskEnvironment {
  [CmdletBinding()]
  param()

  $DotnetCommand = Get-Command dotnet -CommandType Application -ErrorAction Stop |
    Select-Object -First 1
  $DotnetExecutable = $DotnetCommand.Source
  $DotnetRoot = Split-Path -Parent $DotnetExecutable
  $SdkVersion = (& $DotnetExecutable --version | Out-String).Trim()
  $SdksPath = Join-Path $DotnetRoot "sdk\$SdkVersion\Sdks"

  if (-not (Test-Path -LiteralPath $SdksPath -PathType Container)) {
    throw "The selected .NET SDK directory does not exist: $SdksPath"
  }

  return [pscustomobject]@{
    Executable = $DotnetExecutable
    Root = $DotnetRoot
    SdksPath = $SdksPath
  }
}
