# Requires -Version 7.0
# Requires -Modules Pester

Describe 'Ollamactl.PowerShell helper functions' {
  BeforeAll {
    $TestRoot = Join-Path $PSScriptRoot 'temp'
    Remove-Item -LiteralPath $TestRoot -Recurse -Force -ErrorAction SilentlyContinue
    New-Item -ItemType Directory -Path $TestRoot | Out-Null

    . "$(Split-Path -Parent $PSScriptRoot)\Ollamactl.PowerShell\Private\Get-OllamaPaths.ps1"
    . "$(Split-Path -Parent $PSScriptRoot)\Ollamactl.PowerShell\Private\Get-OllamaConfig.ps1"
    . "$(Split-Path -Parent $PSScriptRoot)\Ollamactl.PowerShell\Private\Write-Log.ps1"
    . "$(Split-Path -Parent $PSScriptRoot)\Ollamactl.PowerShell\Private\Import-DotEnv.ps1"
  }

  AfterAll {
    Remove-Item -LiteralPath $TestRoot -Recurse -Force -ErrorAction SilentlyContinue
  }

  Context 'Get-OllamaPaths' {
    It 'returns valid default directories based on explicit home path' {
      $paths = Get-OllamaPaths -HomePath $TestRoot

      $paths.Home | Should -Be $TestRoot
      $paths.ConfigDir | Should -Be (Join-Path $TestRoot '.config\ollama')
      $paths.LogDirectory | Should -Be (Join-Path $paths.ConfigDir 'logs')
      $paths.ModelsDirectory | Should -Be (Join-Path $TestRoot '.ollama\models')
      $paths.PidFile | Should -Be (Join-Path $paths.PidDirectory 'ollama.pid')
    }

    It 'returns custom models directory when OLLAMA_MODELS is set' {
      $env:OLLAMA_MODELS = 'C:\Custom\Models'

      $paths = Get-OllamaPaths -HomePath $TestRoot

      $paths.ModelsDirectory | Should -Be 'C:\Custom\Models'

      Remove-Item Env:\OLLAMA_MODELS -ErrorAction SilentlyContinue
    }
  }

  Context 'Get-OllamaConfig' {
    It 'returns configuration object with defaults and derived paths' {
      $env:OLLAMA_HOST = 'localhost:11434'
      $env:OLLAMA_STARTUP_TIMEOUT = '45'
      $env:OLLAMA_KEEP_ALIVE = '15m'
      $env:OLLAMA_MODEL_LOAD_TIMEOUT = '90'
      $env:OLLAMA_LOG_LEVEL = 'Debug'

      $config = Get-OllamaConfig -HomePath $TestRoot

      $config.Server.Host | Should -Be 'localhost:11434'
      $config.Server.StartupTimeoutSeconds | Should -Be 45
      $config.Models.KeepAlive | Should -Be '15m'
      $config.Models.LoadTimeoutSeconds | Should -Be 90
      $config.Logging.Level | Should -Be 'Debug'
      $config.Paths.LogDirectory | Should -Be (Join-Path $TestRoot '.config\ollama\logs')

      Remove-Item Env:\OLLAMA_HOST -ErrorAction SilentlyContinue
      Remove-Item Env:\OLLAMA_STARTUP_TIMEOUT -ErrorAction SilentlyContinue
      Remove-Item Env:\OLLAMA_KEEP_ALIVE -ErrorAction SilentlyContinue
      Remove-Item Env:\OLLAMA_MODEL_LOAD_TIMEOUT -ErrorAction SilentlyContinue
      Remove-Item Env:\OLLAMA_LOG_LEVEL -ErrorAction SilentlyContinue
    }
  }

  Context 'Import-DotEnv' {
    It 'imports environment variables from a .env file and exports them to process' {
      $envFile = Join-Path $TestRoot '.config\ollama\test.env'
      New-Item -ItemType Directory -Path (Split-Path $envFile) -Force | Out-Null

      @"
# Comment line
KEY_ONE = value1
KEY_TWO=value2
"@ | Set-Content -Path $envFile -NoNewline

            $variables = Import-DotEnv -Path $envFile -ExportToProcess

            $variables.KEY_ONE | Should -Be 'value1'
            $variables.KEY_TWO | Should -Be 'value2'
            $env:KEY_ONE | Should -Be 'value1'
            $env:KEY_TWO | Should -Be 'value2'

            Remove-Item Env:\KEY_ONE -ErrorAction SilentlyContinue
            Remove-Item Env:\KEY_TWO -ErrorAction SilentlyContinue
        }

        It 'throws for invalid lines without assignment' {
            $envFile = Join-Path $TestRoot '.config\ollama\invalid.env'
            New-Item -ItemType Directory -Path (Split-Path $envFile) -Force | Out-Null
            'INVALID_LINE' | Set-Content -Path $envFile -NoNewline

            { Import-DotEnv -Path $envFile } | Should -Throw
        }
    }

    Context 'Write-Log' {
        It 'writes a log entry to the application log file' {
            $message = 'Test log message'
            Write-Log -Level 'Information' -Message $message -HomePath $TestRoot

            $logsPath = Join-Path $TestRoot '.config\ollama\logs\application.log'
            Test-Path $logsPath | Should -BeTrue

            $content = Get-Content -Path $logsPath -Raw
            $content | Should -Match '\[Information\] Test log message'
        }
    }
}
