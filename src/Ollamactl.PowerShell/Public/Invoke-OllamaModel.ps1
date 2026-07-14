#!/usr/bin/env pwsh

[CmdletBinding()]
param(
  [Parameter(Mandatory, Position = 0)]
  [string]$Model,

  [string]$OllamaHost = "http://localhost:11434",

  [ValidateRange(1, 3600)]
  [int]$LoadTimeoutSeconds = 120
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$ApiBase = "$OllamaHost/api"

function Write-Info {
  param([string]$Message)
  Write-Host "[INFO ] $Message"
}

function Invoke-OllamaApi {
  param(
    [ValidateSet("GET","POST")]
    [string]$Method,

    [string]$Path,

    [object]$Body = $null,

    [int]$TimeoutSec = 30
  )

  $params = @{
    Uri = "$ApiBase/$Path"
    Method = $Method
    TimeoutSec = $TimeoutSec
    ErrorAction = "Stop"
  }

  if ($null -ne $Body) {
    $params.ContentType = "application/json"
    $params.Body = $Body | ConvertTo-Json -Depth 10
  }

  Invoke-RestMethod @params
}

function Test-OllamaServer {
  try {
    Invoke-OllamaApi `
      -Method GET `
      -Path "version" `
      -TimeoutSec 5 |
      Out-Null

    return $true
  }
  catch {
    return $false
  }
}

function Get-OllamaModels {

  $response = Invoke-OllamaApi `
    -Method GET `
    -Path "tags"

  if ($null -eq $response) {
    return @()
  }

  if (
    $response.PSObject.Properties.Name -contains "models"
  ) {
    return @($response.models)
  }

  return @()
}

function Get-OllamaRunningModels {

  $response = Invoke-OllamaApi `
    -Method GET `
    -Path "ps"

  if ($null -eq $response) {
    return @()
  }

  if (
    $response.PSObject.Properties.Name -contains "models"
  ) {
    return @($response.models)
  }

  return @()
}

function Find-OllamaModel {
  param(
    [string]$Name
  )

  $models = @(Get-OllamaModels)

  foreach ($model in $models) {

    if ($null -eq $model) {
      continue
    }

    if (
      $model.PSObject.Properties.Name -contains "name"
    ) {

      if ($model.name -ieq $Name) {
        return $model
      }
    }
  }

  return $null
}

function Test-OllamaModelLoaded {
  param(
    [string]$Name
  )

  $running = @(Get-OllamaRunningModels)

  foreach ($model in $running) {

    if ($null -eq $model) {
      continue
    }

    if (
      $model.PSObject.Properties.Name -contains "name"
    ) {

      if ($model.name -ieq $Name) {
        return $true
      }
    }
  }

  return $false
}

function Wait-OllamaModel {
  param(
    [string]$Name,

    [int]$TimeoutSeconds
  )

  $sw = [System.Diagnostics.Stopwatch]::StartNew()

  while (
    $sw.Elapsed.TotalSeconds -lt $TimeoutSeconds
  ) {

    if (
      Test-OllamaModelLoaded -Name $Name
    ) {
      return $true
    }

    Start-Sleep -Milliseconds 500
  }

  return $false
}

function Start-OllamaModelInternal {
  param(
    [string]$Name
  )

  $payload = @{
    model = $Name
    prompt = "hello"
    stream = $false
    keep_alive = "30m"
  }

  Invoke-OllamaApi `
    -Method POST `
    -Path "generate" `
    -Body $payload `
    -TimeoutSec 300 |
    Out-Null
}

function Test-OllamaInference {
  param(
    [string]$Name
  )

  $payload = @{
    model = $Name
    prompt = "OK"
    stream = $false
  }

  $response = Invoke-OllamaApi `
    -Method POST `
    -Path "generate" `
    -Body $payload `
    -TimeoutSec 300

  if (
    $response.PSObject.Properties.Name -contains "done"
  ) {
    return ($response.done -eq $true)
  }

  return $false
}

try {

  Write-Info "Checking Ollama server..."

  if (-not (Test-OllamaServer)) {
    throw "Cannot connect to $OllamaHost"
  }

  Write-Info "Ollama server available."

  $models = @(Get-OllamaModels)

  Write-Info "Detected models:"

  if ($models.Length -eq 0) {

    Write-Warning `
      "No models returned by /api/tags"

    Write-Host ""
    Write-Host "Raw API response:"

    Invoke-OllamaApi `
      -Method GET `
      -Path "tags" |
      ConvertTo-Json -Depth 20

    exit 1
  }

  foreach ($m in $models) {

    if (
      $m.PSObject.Properties.Name -contains "name"
    ) {
      Write-Host "  - $($m.name)"
    }
    else {
      Write-Host "  - [unknown model format]"
      $m | ConvertTo-Json -Depth 5
    }
  }

  $found = Find-OllamaModel -Name $Model

  if ($null -eq $found) {

    throw "Model '$Model' not found."
  }

  Write-Info "Model found."

  if (-not (
      Test-OllamaModelLoaded -Name $Model
    )) {

    Write-Info "Loading model..."

    Start-OllamaModelInternal `
      -Name $Model

    Write-Info `
      "Waiting for model readiness..."

    if (-not (
        Wait-OllamaModel `
          -Name $Model `
          -TimeoutSeconds $LoadTimeoutSeconds
      )) {

      throw "Timed out waiting for model load."
    }
  }

  Write-Info "Validating inference..."

  if (-not (
      Test-OllamaInference `
        -Name $Model
    )) {

    throw "Inference validation failed."
  }

  Write-Info "Model ready."

  [pscustomobject]@{
    Model = $Model
    Host = $OllamaHost
    Status = "Ready"
    Timestamp = Get-Date
  }
}
catch {
  Write-Error $_.Exception.Message
  exit 1
}
