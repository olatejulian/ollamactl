function Get-OllamaConfig {

  [CmdletBinding()]
  [OutputType([pscustomobject])]
  param(
    [string]$HomePath
  )

  $Paths = Get-OllamaPaths -HomePath $HomePath

  [pscustomobject]@{

    Server = [pscustomobject]@{

      Host = if ($env:OLLAMA_HOST) { $env:OLLAMA_HOST } else { "127.0.0.1:11434" }

      StartupTimeoutSeconds = if ($env:OLLAMA_STARTUP_TIMEOUT) {
        [int]$env:OLLAMA_STARTUP_TIMEOUT
      } else {
        30
      }
    }

    Models = [pscustomobject]@{

      KeepAlive = if ($env:OLLAMA_KEEP_ALIVE) {
        $env:OLLAMA_KEEP_ALIVE
      } else {
        "30m"
      }

      LoadTimeoutSeconds = if ($env:OLLAMA_MODEL_LOAD_TIMEOUT) {
        [int]$env:OLLAMA_MODEL_LOAD_TIMEOUT
      } else {
        120
      }
    }

    Logging = [pscustomobject]@{

      Level = if ($env:OLLAMA_LOG_LEVEL) {
        $env:OLLAMA_LOG_LEVEL
      } else {
        "Information"
      }

      Directory = $Paths.LogDirectory
    }

    Paths = $Paths
  }
}
