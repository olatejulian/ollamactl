#!/usr/bin/env pwsh

Set-StrictMode -Version Latest

$ErrorActionPreference = "Stop"

$ConfigDir = Join-Path $HOME ".config\ollama"
$EnvFile   = Join-Path $ConfigDir "ollama.env"

$LogDir    = Join-Path $ConfigDir "logs"

$StdOutLog = Join-Path $LogDir "ollama.out.log"
$StdErrLog = Join-Path $LogDir "ollama.err.log"

function Write-Info {
    param([string]$Message)
    Write-Host "[INFO] $Message"
}

function Write-FailAndExit {
    param(
        [string]$Message,
        [int]$ExitCode = 1
    )

    Write-Error $Message
    exit $ExitCode
}

function Test-OllamaRunning {

    $Process = Get-Process `
        -Name "ollama" `
        -ErrorAction SilentlyContinue

    return $null -ne $Process
}

function Import-DotEnv {

    param(
        [Parameter(Mandatory)]
        [string]$Path
    )

    if (-not (Test-Path $Path)) {
        throw "Environment file not found: $Path"
    }

    Get-Content $Path | ForEach-Object {

        $Line = $_.Trim()

        if (
            [string]::IsNullOrWhiteSpace($Line) -or
            $Line.StartsWith("#")
        ) {
            return
        }

        if ($Line -notmatch "=") {
            throw "Invalid env line: $Line"
        }

        $Key, $Value = $Line -split '=', 2

        $Key = $Key.Trim()
        $Value = $Value.Trim()

        if ([string]::IsNullOrWhiteSpace($Key)) {
            throw "Invalid variable name."
        }

        [Environment]::SetEnvironmentVariable(
            $Key,
            $Value,
            "Process"
        )
    }
}

try {

    if (Test-OllamaRunning) {

        Write-Info "Ollama is already running."
        exit 0
    }

    New-Item `
        -ItemType Directory `
        -Force `
        -Path $LogDir | Out-Null

    Import-DotEnv -Path $EnvFile

    $OllamaExe = Get-Command ollama `
        -ErrorAction Stop

    $Process = Start-Process `
        -FilePath $OllamaExe.Source `
        -ArgumentList "serve" `
        -RedirectStandardOutput $StdOutLog `
        -RedirectStandardError $StdErrLog `
        -PassThru `
        -WindowStyle Hidden

    Start-Sleep -Seconds 3

    if ($Process.HasExited) {

        Write-FailAndExit `
            "Ollama exited immediately. Check logs."
    }

    Write-Info "Ollama started."
    Write-Info "PID: $($Process.Id)"
    Write-Info "STDOUT: $StdOutLog"
    Write-Info "STDERR: $StdErrLog"

    exit 0
}
catch {

    Write-FailAndExit $_.Exception.Message
}