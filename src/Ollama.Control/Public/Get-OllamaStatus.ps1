#!/usr/bin/env pwsh

Set-StrictMode -Version Latest

$ErrorActionPreference = "Stop"


#
# Configuration
#

$OllamaHost = "http://localhost:11434"
$OllamaApi = "$OllamaHost/api"

$OllamaConfigDir = Join-Path `
    $HOME `
    ".config\ollama"

$EnvFile = Join-Path `
    $OllamaConfigDir `
    "ollama.env"


#
# Logging
#

function Write-Info {
    param([string]$Message)

    Write-Host "[INFO ] $Message"
}


function Write-Warn {
    param([string]$Message)

    Write-Host "[WARN ] $Message"
}


function Write-Fail {
    param([string]$Message)

    Write-Host "[ERROR] $Message"
}


function Format-Bytes {
    param([long]$Bytes)

    if ($Bytes -ge 1GB) {
        return "{0:N2} GB" -f ($Bytes / 1GB)
    }

    if ($Bytes -ge 1MB) {
        return "{0:N2} MB" -f ($Bytes / 1MB)
    }

    return "$Bytes bytes"
}


function Get-ProcessMemory {

    param(
        [System.Diagnostics.Process[]]$Processes
    )

    $Total = 0

    foreach ($Process in $Processes) {

        if ($null -ne $Process) {

            $Total += $Process.WorkingSet64
        }
    }

    return $Total
}


function Read-EnvFile {

    param(
        [string]$Path
    )

    $Variables = @{}

    if (-not (Test-Path $Path)) {

        return $Variables
    }


    foreach ($Line in Get-Content $Path) {

        $Line = $Line.Trim()


        if (
            $Line.Length -eq 0 -or
            $Line.StartsWith("#")
        ) {
            continue
        }


        $Parts = $Line.Split(
            "=",
            2
        )


        if ($Parts.Count -eq 2) {

            $Key = $Parts[0].Trim()
            $Value = $Parts[1].Trim()

            $Variables[$Key] = $Value
        }
    }


    return $Variables
}


#
# Main
#

$ExitCode = 0


try {

    Write-Host ""
    Write-Host "========================================"
    Write-Host "           OLLAMA HEALTH CHECK"
    Write-Host "========================================"
    Write-Host ""


    #
    # Ollama Process
    #

    $OllamaProcess = Get-Process `
        -Name "ollama" `
        -ErrorAction SilentlyContinue |
    Select-Object -First 1


    if ($null -eq $OllamaProcess) {

        Write-Fail "Ollama process not found."
        exit 2
    }


    Write-Info "Ollama server running."
    Write-Info "PID: $($OllamaProcess.Id)"


    #
    # llama-server
    #

    $LlamaProcesses = Get-Process `
        -Name "llama-server" `
        -ErrorAction SilentlyContinue


    if ($null -eq $LlamaProcesses) {

        Write-Info "No active llama-server process."
    }
    else {

        Write-Info `
            "Active llama-server: $($LlamaProcesses.Count)"
    }


    #
    # Memory
    #

    $Processes = @(
        $OllamaProcess
        $LlamaProcesses
    )


    $Memory = Get-ProcessMemory `
        -Processes $Processes


    Write-Info `
        "Total RAM Usage: $(Format-Bytes $Memory)"


    #
    # Network
    #

    $Connection = Get-NetTCPConnection `
        -State Listen `
        -ErrorAction SilentlyContinue |
    Where-Object {
        $_.LocalPort -eq 11434
    } |
    Select-Object -First 1


    if ($null -eq $Connection) {

        Write-Fail `
            "Port 11434 is not listening."

        exit 2
    }


    Write-Info `
        "Listening on $($Connection.LocalAddress):$($Connection.LocalPort)"


    #
    # API
    #

    try {

        $Tags = Invoke-RestMethod `
            -Uri "$OllamaApi/tags" `
            -TimeoutSec 10


        Write-Info "API responding."

    }
    catch {

        Write-Fail "API unavailable."
        exit 2
    }


    #
    # Installed Models
    #

    Write-Host ""
    Write-Info "Installed Models"


    foreach ($Model in $Tags.models) {

        Write-Host `
            "   - $($Model.name) ($(Format-Bytes $Model.size))"
    }


    #
    # Loaded Models
    #

    Write-Host ""
    Write-Info "Loaded Models"


    try {

        $Running = Invoke-RestMethod `
            -Uri "$OllamaApi/ps" `
            -TimeoutSec 10


        if ($Running.models.Count -eq 0) {

            Write-Host "   <none>"
        }
        else {

            foreach ($Model in $Running.models) {

                Write-Host ""
                Write-Host `
                    "   - $($Model.name)"

                if ($Model.size) {

                    Write-Host `
                        "     Size: $(Format-Bytes $Model.size)"
                }


                if ($Model.size_vram) {

                    Write-Host `
                        "     VRAM: $(Format-Bytes $Model.size_vram)"
                }


                if ($Model.expires_at) {

                    Write-Host `
                        "     Expires: $($Model.expires_at)"
                }
            }
        }

    }
    catch {

        Write-Warn `
            "Unable to query loaded models."

        $ExitCode = 1
    }


    #
    # Configuration
    #

    Write-Host ""
    Write-Info "Configuration"


    if (Test-Path $EnvFile) {

        Write-Info `
            "Environment file: $EnvFile"


        $Env = Read-EnvFile `
            -Path $EnvFile


        foreach ($Key in $Env.Keys) {

            Write-Host `
                "   $Key=$($Env[$Key])"
        }

    }
    else {

        Write-Warn `
            "No ollama.env found."

        $ExitCode = 1
    }


    #
    # GPU
    #

    Write-Host ""
    Write-Info "GPU"


    $Gpu = Get-CimInstance `
        Win32_VideoController |
    Where-Object {
        $_.Name -match "NVIDIA"
    } |
    Select-Object -First 1


    if ($Gpu) {

        Write-Info $Gpu.Name

    }
    else {

        Write-Warn `
            "NVIDIA GPU not detected."

        $ExitCode = 1
    }


    #
    # NVIDIA SMI
    #

    if (Get-Command nvidia-smi -ErrorAction SilentlyContinue) {

        Write-Host ""
        Write-Info "NVIDIA Status"

        try {

            nvidia-smi

        }
        catch {

            Write-Warn `
                "Unable to execute nvidia-smi."

            $ExitCode = 1
        }
    }


    #
    # Version
    #

    Write-Host ""
    Write-Info "Version"


    try {

        $Version = Invoke-RestMethod `
            -Uri "$OllamaApi/version"


        Write-Host `
            "   Ollama: $($Version.version)"

    }
    catch {

        Write-Warn `
            "Unable to query version."
    }


    #
    # Result
    #

    Write-Host ""
    Write-Host "========================================"


    switch ($ExitCode) {

        0 {
            Write-Host "STATUS: HEALTHY"
        }

        1 {
            Write-Host "STATUS: WARNING"
        }

        default {
            Write-Host "STATUS: ERROR"
        }
    }


    Write-Host "========================================"
    Write-Host ""


    exit $ExitCode

}
catch {

    Write-Fail $_.Exception.Message

    exit 2
}