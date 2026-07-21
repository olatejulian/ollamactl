function Get-OllamaHomePath {

  [CmdletBinding()]
  [OutputType([string])]
  param(
    [string]$HomePath
  )

  if ([string]::IsNullOrWhiteSpace($HomePath)) {
    $HomePath = [Environment]::GetEnvironmentVariable('HOME', 'Process')
  }

  if ([string]::IsNullOrWhiteSpace($HomePath)) {
    $HomePath = $HOME
  }

  if ([string]::IsNullOrWhiteSpace($HomePath)) {
    $HomePath = [Environment]::GetFolderPath('UserProfile')
  }

  return $HomePath
}

function Get-OllamaPaths {

  [CmdletBinding()]
  [OutputType([pscustomobject])]
  param(
    [string]$HomePath
  )

  $HomePath = Get-OllamaHomePath -HomePath $HomePath
  $ConfigDir = Join-Path $HomePath ".config\ollama"
  $EnvFile = Join-Path $ConfigDir ".env"
  $LogDirectory = Join-Path $ConfigDir "logs"
  $CacheDirectory = Join-Path $ConfigDir "cache"
  $PidDirectory = Join-Path $ConfigDir "run"
  $PidFile = Join-Path $PidDirectory "ollama.pid"

  # Models directory follows OLLAMA_MODELS (env or .env) and falls back
  # to the Ollama default when neither is set.
  $ModelsDirectory = if ($env:OLLAMA_MODELS) {
    $env:OLLAMA_MODELS
  } else {
    Join-Path $HomePath ".ollama\models"
  }

  [pscustomobject]@{
    Home = $HomePath
    ConfigDir = $ConfigDir
    EnvFile = $EnvFile
    LogDirectory = $LogDirectory
    CacheDirectory = $CacheDirectory
    PidDirectory = $PidDirectory
    PidFile = $PidFile
    ModelsDirectory = $ModelsDirectory
  }
}
