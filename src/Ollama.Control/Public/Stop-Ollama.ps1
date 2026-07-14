#!/usr/bin/env pwsh

Set-StrictMode -Version Latest

$ErrorActionPreference = "Stop"

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

try {

    $Processes = Get-Process `
        -Name "ollama" `
        -ErrorAction SilentlyContinue

    if (-not $Processes) {

        Write-Info "Ollama is not running."
        exit 0
    }

    foreach ($Process in $Processes) {

        Write-Info "Stopping PID $($Process.Id)"

        Stop-Process `
            -Id $Process.Id `
            -ErrorAction Stop
    }

    Write-Info "Ollama stopped."

    exit 0
}
catch {

    Write-FailAndExit $_.Exception.Message
}