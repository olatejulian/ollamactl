<#
.SYNOPSIS
Builds and packages ollamactl from the repository root.

.DESCRIPTION
Forwards build parameters to scripts/Build.ps1, which performs formatting,
compilation, testing, staged publishing, end-to-end validation, and packaging.

.PARAMETER Configuration
The .NET build configuration. The default is Release.

.PARAMETER Runtime
The Windows runtime identifier to publish. The default is win-x64.

.PARAMETER SkipTests
Skips the pre-publish test suite. End-to-end tests for the staged executable
remain mandatory before artifact promotion.

.PARAMETER NativeAot
Publishes a Native AOT executable instead of a trimmed single-file executable.

.EXAMPLE
./build.ps1

Builds, tests, and packages the default win-x64 single-file artifact.

.EXAMPLE
./build.ps1 -NativeAot

Builds, tests, and packages the win-x64 Native AOT artifact.
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

& (Join-Path $PSScriptRoot 'scripts\Build.ps1') @PSBoundParameters
