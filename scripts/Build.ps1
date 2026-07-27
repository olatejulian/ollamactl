<#
.SYNOPSIS
Builds, validates, publishes, and packages ollamactl for Windows.

.DESCRIPTION
Restores and formats the solution, builds it, optionally runs the complete test
suite, and publishes a self-contained Windows executable. The published binary
is kept in a staging directory until its end-to-end tests pass. Successful
builds produce the executable, a SHA-256 checksum, an artifact manifest, and a
versioned ZIP under artifacts/<runtime>.

.PARAMETER Configuration
The .NET build configuration. The default is Release.

.PARAMETER Runtime
The Windows runtime identifier to publish. The default is win-x64.

.PARAMETER SkipTests
Skips the pre-publish .NET, Pester, and PSScriptAnalyzer test suite. The staged
executable's end-to-end tests remain mandatory before artifact promotion.

.PARAMETER NativeAot
Publishes a Native AOT executable instead of a trimmed single-file executable.

.EXAMPLE
./scripts/Build.ps1

Builds, tests, and packages a Release single-file executable for win-x64.

.EXAMPLE
./scripts/Build.ps1 -NativeAot -Runtime win-x64

Builds, tests, and packages a Release Native AOT executable for win-x64.
#>
#Requires -Version 7.3

[CmdletBinding()]
param(
  [ValidateSet('Debug', 'Release')]
  [string]$Configuration = 'Release',

  [ValidateSet('win-x64', 'win-arm64')]
  [string]$Runtime = 'win-x64',

  [switch]$SkipTests,

  [switch]$NativeAot
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $true

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

function Assert-ChildPath {
  [CmdletBinding()]
  param(
    [Parameter(Mandatory)]
    [string]$Path,

    [Parameter(Mandatory)]
    [string]$Parent
  )

  $ResolvedPath = [System.IO.Path]::GetFullPath($Path)
  $ResolvedParent = [System.IO.Path]::GetFullPath($Parent).TrimEnd(
    [System.IO.Path]::DirectorySeparatorChar,
    [System.IO.Path]::AltDirectorySeparatorChar
  )
  $ParentPrefix = $ResolvedParent + [System.IO.Path]::DirectorySeparatorChar

  if (-not $ResolvedPath.StartsWith(
      $ParentPrefix,
      [System.StringComparison]::OrdinalIgnoreCase
    )) {
    throw "Unsafe child path '$ResolvedPath'; expected a path below '$ResolvedParent'."
  }

  return $ResolvedPath
}

$RepositoryRoot = Split-Path -Parent $PSScriptRoot
$Solution = Join-Path $RepositoryRoot 'ollamactl.slnx'
$CliProject = Join-Path $RepositoryRoot 'src\Ollamactl.Cli\Ollamactl.Cli.csproj'
$CliProjectDirectory = Split-Path -Parent $CliProject
$EndToEndProject = Join-Path $RepositoryRoot 'tests\Ollamactl.EndToEndTests\Ollamactl.EndToEndTests.csproj'
$TestScript = Join-Path $PSScriptRoot 'Test.ps1'
$ArtifactRoot = Join-Path $RepositoryRoot 'artifacts'
$ArtifactDirectory = Join-Path $ArtifactRoot $Runtime
$PublishMode = if ($NativeAot) { 'aot' } else { 'singlefile' }
$StagingRoot = Join-Path $ArtifactRoot '.staging'
$StagingDirectory = Join-Path $StagingRoot ([guid]::NewGuid().ToString('N'))
$PublishDirectory = Join-Path $StagingDirectory 'publish'
$PackageDirectory = Join-Path $StagingDirectory 'package'
$PublishedExecutable = Join-Path $PublishDirectory 'ollamactl.exe'
$PackagedExecutable = Join-Path $PackageDirectory 'ollamactl.exe'
$ChecksumPath = Join-Path $PackageDirectory 'ollamactl.exe.sha256'
$ManifestPath = Join-Path $PackageDirectory 'ollamactl.manifest.json'
$DotnetArtifactsDirectory = Join-Path $StagingDirectory 'dotnet'

$ResolvedStagingDirectory = Assert-ChildPath -Path $StagingDirectory -Parent $ArtifactRoot
$ResolvedStagingRoot = Assert-ChildPath -Path $StagingRoot -Parent $ArtifactRoot
$ResolvedArtifactDirectory = Assert-ChildPath -Path $ArtifactDirectory -Parent $ArtifactRoot
$EnvironmentSnapshot = Get-ProcessEnvironmentSnapshot -Name @(
  'DOTNET_ROOT'
  'DOTNET_ROOT_X64'
  'MSBuildSDKsPath'
  'OLLAMACTL_E2E_EXECUTABLE'
)

try {
  Push-Location $RepositoryRoot
  try {
    # Keep MSBuild task hosts on the SDK/runtime selected by global.json. This
    # also repairs machines whose process environment points at an older SDK.
    $TaskDotnetCommand = Get-Command dotnet -CommandType Application -ErrorAction Stop |
      Select-Object -First 1
    $TaskDotnetExecutable = $TaskDotnetCommand.Source
    $TaskDotnetRoot = Split-Path -Parent $TaskDotnetExecutable
    $TaskSdkVersion = (& $TaskDotnetExecutable --version | Out-String).Trim()
    $TaskSdkDirectory = Join-Path $TaskDotnetRoot "sdk\$TaskSdkVersion"
    $TaskSdksPath = Join-Path $TaskSdkDirectory 'Sdks'

    if (-not (Test-Path -LiteralPath $TaskSdksPath -PathType Container)) {
      throw "The selected .NET SDK directory does not exist: $TaskSdksPath"
    }

    $env:DOTNET_ROOT = $TaskDotnetRoot
    $env:DOTNET_ROOT_X64 = $TaskDotnetRoot
    $env:MSBuildSDKsPath = $TaskSdksPath
    [Environment]::SetEnvironmentVariable(
      'OLLAMACTL_E2E_EXECUTABLE',
      $null,
      'Process'
    )

    dotnet restore $Solution
    dotnet format $Solution --verify-no-changes --no-restore
    dotnet build $Solution -c $Configuration --no-restore

    if (-not $SkipTests) {
      & $TestScript -Configuration $Configuration -NoBuild -NoRestore
    }

    New-Item -ItemType Directory -Path $PublishDirectory -Force | Out-Null

    $PublishArguments = @(
      'publish'
      $CliProject
      '-c', $Configuration
      '-r', $Runtime
      '--self-contained', 'true'
      '-p:DebugType=None'
      '-p:DebugSymbols=false'
      '--artifacts-path', $DotnetArtifactsDirectory
      '-o', $PublishDirectory
    )

    if ($NativeAot) {
      $PublishArguments += '-p:PublishAot=true'
    }
    else {
      $PublishArguments += @(
        '-p:PublishSingleFile=true'
        '-p:IncludeNativeLibrariesForSelfExtract=true'
        '-p:EnableCompressionInSingleFile=true'
        '-p:PublishTrimmed=true'
        '-p:TrimMode=partial'
      )
    }

    Push-Location $CliProjectDirectory
    try {
      dotnet @PublishArguments
    }
    finally {
      Pop-Location
    }

    if (-not (Test-Path -LiteralPath $PublishedExecutable -PathType Leaf)) {
      throw "Expected executable was not created: $PublishedExecutable"
    }

    # The staged executable is the release gate. It is promoted only after the
    # end-to-end suite has exercised this exact file.
    $env:OLLAMACTL_E2E_EXECUTABLE = $PublishedExecutable
    dotnet test `
      $EndToEndProject `
      -c $Configuration `
      --no-build `
      --no-restore `
      --filter 'Category=EndToEnd' `
      --logger 'console;verbosity=normal'

    $ArtifactVersion = (& $PublishedExecutable --version | Out-String).Trim()
    if ([string]::IsNullOrWhiteSpace($ArtifactVersion)) {
      throw 'The staged executable returned an empty version.'
    }

    $SafeArtifactVersion = [regex]::Replace(
      $ArtifactVersion,
      '[^0-9A-Za-z._-]+',
      '-'
    ).Trim('-')
    if ([string]::IsNullOrWhiteSpace($SafeArtifactVersion)) {
      throw "The artifact version cannot be used in a file name: $ArtifactVersion"
    }

    New-Item -ItemType Directory -Path $PackageDirectory -Force | Out-Null
    Copy-Item -LiteralPath $PublishedExecutable -Destination $PackagedExecutable

    $ExecutableItem = Get-Item -LiteralPath $PackagedExecutable
    $ExecutableHash = Get-FileHash -LiteralPath $PackagedExecutable -Algorithm SHA256
    $ChecksumLine = '{0} *{1}' -f $ExecutableHash.Hash.ToLowerInvariant(), $ExecutableItem.Name
    Set-Content -LiteralPath $ChecksumPath -Value $ChecksumLine -Encoding utf8NoBOM

    $CreatedUtc = [DateTimeOffset]::UtcNow.ToString('O')
    $Manifest = [ordered]@{
      schemaVersion = 1
      name = 'ollamactl'
      version = $ArtifactVersion
      runtime = $Runtime
      configuration = $Configuration
      publishMode = $PublishMode
      nativeAot = [bool]$NativeAot
      selfContained = $true
      createdUtc = $CreatedUtc
      executable = [ordered]@{
        path = $ExecutableItem.Name
        bytes = $ExecutableItem.Length
        sha256 = $ExecutableHash.Hash.ToLowerInvariant()
      }
    }
    $Manifest |
      ConvertTo-Json -Depth 4 |
      Set-Content -LiteralPath $ManifestPath -Encoding utf8NoBOM

    $ZipName = "ollamactl-$SafeArtifactVersion-$Runtime-$PublishMode.zip"
    $ZipPath = Join-Path $PackageDirectory $ZipName
    Compress-Archive `
      -LiteralPath @($PackagedExecutable, $ChecksumPath, $ManifestPath) `
      -DestinationPath $ZipPath `
      -CompressionLevel Optimal

    # Replace only the runtime-specific, generated artifact directory, and do
    # so only after the staged executable and package are complete.
    New-Item -ItemType Directory -Path $ArtifactRoot -Force | Out-Null
    if (Test-Path -LiteralPath $ResolvedArtifactDirectory) {
      Remove-Item -LiteralPath $ResolvedArtifactDirectory -Recurse -Force
    }
    Move-Item -LiteralPath $PackageDirectory -Destination $ResolvedArtifactDirectory

    $FinalExecutable = Join-Path $ResolvedArtifactDirectory 'ollamactl.exe'
    $FinalChecksum = Join-Path $ResolvedArtifactDirectory 'ollamactl.exe.sha256'
    $FinalManifest = Join-Path $ResolvedArtifactDirectory 'ollamactl.manifest.json'
    $FinalZip = Join-Path $ResolvedArtifactDirectory $ZipName

    [pscustomobject]@{
      Path = $FinalExecutable
      ChecksumPath = $FinalChecksum
      ManifestPath = $FinalManifest
      ZipPath = $FinalZip
      Version = $ArtifactVersion
      Runtime = $Runtime
      PublishMode = $PublishMode
      Bytes = $ExecutableItem.Length
      Sha256 = $ExecutableHash.Hash
      NativeAot = [bool]$NativeAot
    }
  }
  finally {
    Pop-Location
  }
}
finally {
  if (Test-Path -LiteralPath $ResolvedStagingDirectory) {
    Remove-Item -LiteralPath $ResolvedStagingDirectory -Recurse -Force
  }

  if (Test-Path -LiteralPath $ResolvedStagingRoot -PathType Container) {
    $StagingEntry = Get-ChildItem -LiteralPath $ResolvedStagingRoot -Force |
      Select-Object -First 1
    if ($null -eq $StagingEntry) {
      Remove-Item -LiteralPath $ResolvedStagingRoot -Force -ErrorAction SilentlyContinue
    }
  }

  Restore-ProcessEnvironment -Snapshot $EnvironmentSnapshot
}
