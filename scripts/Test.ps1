<#
.SYNOPSIS
Runs the ollamactl automated test and PowerShell quality suites.

.DESCRIPTION
Runs all .NET tests in the solution, executes Pester using the repository's
PesterConfiguration.psd1 when present, and runs PSScriptAnalyzer with repository
settings when the module is available. If no analyzer settings file exists, a
strict Error/Warning baseline is used. A clear warning is emitted when
PSScriptAnalyzer is not installed.

.PARAMETER Configuration
The .NET build configuration whose tests should run. The default is Release.

.PARAMETER NoBuild
Passes --no-build to dotnet test.

.PARAMETER NoRestore
Passes --no-restore to dotnet test.

.EXAMPLE
./scripts/Test.ps1

Restores as needed, builds as needed, and runs every automated test suite.

.EXAMPLE
./scripts/Test.ps1 -Configuration Release -NoBuild -NoRestore

Runs tests against an already restored and built Release solution.
#>
#Requires -Version 7.3

[CmdletBinding()]
param(
  [ValidateSet('Debug', 'Release')]
  [string]$Configuration = 'Release',

  [switch]$NoBuild,

  [switch]$NoRestore
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $true

$RepositoryRoot = Split-Path -Parent $PSScriptRoot
$Solution = Join-Path $RepositoryRoot 'ollamactl.slnx'
$PesterTests = Join-Path $RepositoryRoot 'tests\PowerShell'
$PesterConfigurationPath = Join-Path $RepositoryRoot 'PesterConfiguration.psd1'
$AnalyzerSettingsCandidates = @(
  (Join-Path $RepositoryRoot 'PSScriptAnalyzerSettings.psd1')
  (Join-Path $RepositoryRoot '.config\PSScriptAnalyzerSettings.psd1')
)
$DotnetEnvironmentSnapshot = @{}
foreach ($VariableName in @('DOTNET_ROOT', 'DOTNET_ROOT_X64', 'MSBuildSDKsPath')) {
  $DotnetEnvironmentSnapshot[$VariableName] = [Environment]::GetEnvironmentVariable(
    $VariableName,
    'Process'
  )
}

$TaskDotnetCommand = Get-Command dotnet -CommandType Application -ErrorAction Stop |
  Select-Object -First 1
$TaskDotnetExecutable = $TaskDotnetCommand.Source
$TaskDotnetRoot = Split-Path -Parent $TaskDotnetExecutable
$TaskSdkVersion = (& $TaskDotnetExecutable --version | Out-String).Trim()
$TaskSdksPath = Join-Path $TaskDotnetRoot "sdk\$TaskSdkVersion\Sdks"
if (-not (Test-Path -LiteralPath $TaskSdksPath -PathType Container)) {
  throw "The selected .NET SDK directory does not exist: $TaskSdksPath"
}

Push-Location $RepositoryRoot
try {
  $env:DOTNET_ROOT = $TaskDotnetRoot
  $env:DOTNET_ROOT_X64 = $TaskDotnetRoot
  $env:MSBuildSDKsPath = $TaskSdksPath

  $DotnetTestArguments = @(
    'test'
    $Solution
    '-c', $Configuration
    '--logger', 'console;verbosity=normal'
  )
  if ($NoBuild) {
    $DotnetTestArguments += '--no-build'
  }
  if ($NoRestore) {
    $DotnetTestArguments += '--no-restore'
  }

  & $TaskDotnetExecutable @DotnetTestArguments

  $PesterModule = Get-Module -ListAvailable -Name Pester |
    Sort-Object Version -Descending |
    Select-Object -First 1
  if ($null -eq $PesterModule) {
    throw 'Pester is not installed. Install Pester 5 or later to run the PowerShell tests.'
  }

  Import-Module $PesterModule.Path -Force
  if (Test-Path -LiteralPath $PesterConfigurationPath -PathType Leaf) {
    $PesterConfigurationData = Import-PowerShellDataFile -LiteralPath $PesterConfigurationPath
    $PesterConfiguration = New-PesterConfiguration -Hashtable $PesterConfigurationData
    $PesterConfiguration.Run.PassThru = $true
    $PesterResult = Invoke-Pester -Configuration $PesterConfiguration
  }
  else {
    $PesterResult = Invoke-Pester `
      -Path $PesterTests `
      -Output Detailed `
      -PassThru
  }

  if ($null -eq $PesterResult) {
    throw 'Pester did not return a test result.'
  }
  if ($PesterResult.FailedCount -gt 0 -or $PesterResult.Result -ne 'Passed') {
    throw "Pester failed: $($PesterResult.FailedCount) failing test(s)."
  }

  $AnalyzerModule = Get-Module -ListAvailable -Name PSScriptAnalyzer |
    Sort-Object Version -Descending |
    Select-Object -First 1
  if ($null -eq $AnalyzerModule) {
    Write-Warning (
      'PSScriptAnalyzer is not installed; PowerShell static analysis was skipped. ' +
      'Install-Module PSScriptAnalyzer -Scope CurrentUser'
    )
  }
  else {
    Import-Module $AnalyzerModule.Path -Force
    $AnalyzerSettingsPath = $AnalyzerSettingsCandidates |
      Where-Object { Test-Path -LiteralPath $_ -PathType Leaf } |
      Select-Object -First 1
    $AnalyzerSettings = if ($null -ne $AnalyzerSettingsPath) {
      $AnalyzerSettingsPath
    }
    else {
      @{
        IncludeDefaultRules = $true
        Severity = @('Error', 'Warning')
      }
    }

    # Analyze the shipped module and the build/test entry points that maintain
    # this repository.
    $AnalyzerTargets = @(
      (Join-Path $RepositoryRoot 'build.ps1')
      (Join-Path $RepositoryRoot 'scripts\Build.ps1')
      (Join-Path $RepositoryRoot 'scripts\Test.ps1')
      (Join-Path $RepositoryRoot 'src\Ollamactl.PowerShell')
    ) | Where-Object { Test-Path -LiteralPath $_ }

    $AnalyzerResults = foreach ($AnalyzerTarget in $AnalyzerTargets) {
      $AnalyzerParameters = @{
        Path = $AnalyzerTarget
        Settings = $AnalyzerSettings
      }
      if (Test-Path -LiteralPath $AnalyzerTarget -PathType Container) {
        $AnalyzerParameters.Recurse = $true
      }

      Invoke-ScriptAnalyzer @AnalyzerParameters
    }

    if (@($AnalyzerResults).Count -gt 0) {
      $AnalyzerReport = $AnalyzerResults |
        Sort-Object ScriptName, Line, RuleName |
        Format-Table Severity, ScriptName, Line, RuleName, Message -AutoSize |
        Out-String
      Write-Output $AnalyzerReport
      throw "PSScriptAnalyzer reported $(@($AnalyzerResults).Count) issue(s)."
    }
  }
}
finally {
  Pop-Location
  foreach ($Entry in $DotnetEnvironmentSnapshot.GetEnumerator()) {
    [Environment]::SetEnvironmentVariable(
      [string]$Entry.Key,
      $Entry.Value,
      'Process'
    )
  }
}
