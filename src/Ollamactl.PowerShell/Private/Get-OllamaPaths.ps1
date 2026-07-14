function Get-OllamaPaths {

  [CmdletBinding()]
  [OutputType([pscustomobject])]
  param()

  $ConfigDir = Join-Path $HOME ".config\ollama"
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
    Join-Path $HOME ".ollama\models"
  }

  [pscustomobject]@{
    Home = $HOME
    ConfigDir = $ConfigDir
    EnvFile = $EnvFile
    LogDirectory = $LogDirectory
    CacheDirectory = $CacheDirectory
    PidDirectory = $PidDirectory
    PidFile = $PidFile
    ModelsDirectory = $ModelsDirectory
  }
}
