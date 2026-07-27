#Requires -Version 7.2
#Requires -Modules Pester

$ModuleManifest = Join-Path $PSScriptRoot '..\..\src\Ollamactl.PowerShell\Ollamactl.PowerShell.psd1'
if (-not (Get-Module -Name Ollamactl.PowerShell)) {
  Import-Module $ModuleManifest
}

Describe 'Ollamactl.PowerShell structured commands' {
  InModuleScope Ollamactl.PowerShell {
    BeforeEach {
      Mock Invoke-OllamactlJson {
        [pscustomobject]@{ Success = $true }
      }
    }

    It 'maps endpoint options to separate JSON CLI arguments' {
      $Result = Get-OllamaEndpoint `
        -OllamaHost 'http://127.0.0.1:11434' `
        -TimeoutSeconds 15 `
        -ConfigDirectory 'C:\Config With Spaces' `
        -ExecutablePath 'C:\Tools\ollamactl.exe'

      $Result.Success | Should -BeTrue
      Should -Invoke Invoke-OllamactlJson -Times 1 -Exactly -ParameterFilter {
        ($ArgumentList -join '|') -eq
          '--output|json|--host|http://127.0.0.1:11434|--timeout|15|--config-dir|C:\Config With Spaces|endpoint' -and
        $ExecutablePath -eq 'C:\Tools\ollamactl.exe'
      }
    }

    It 'maps the complete health command including bounded logs' {
      $Result = Get-OllamaHealth -IncludeLogs -LogTail 200

      $Result.Success | Should -BeTrue
      Should -Invoke Invoke-OllamactlJson -Times 1 -Exactly -ParameterFilter {
        ($ArgumentList -join '|') -eq '--output|json|health|--include-logs|--log-tail|200' -and
        $AllowNonZeroExitCode
      }
    }

    It 'rejects a log tail when logs were not requested' {
      { Get-OllamaHealth -LogTail 10 } | Should -Throw '*requires -IncludeLogs*'
      Should -Invoke Invoke-OllamactlJson -Times 0 -Exactly
    }

    It 'maps installed and running model queries' {
      $null = Get-OllamaModel
      $null = Get-OllamaModel -Running

      Should -Invoke Invoke-OllamactlJson -Times 1 -Exactly -ParameterFilter {
        ($ArgumentList -join '|') -eq '--output|json|model|list'
      }
      Should -Invoke Invoke-OllamactlJson -Times 1 -Exactly -ParameterFilter {
        ($ArgumentList -join '|') -eq '--output|json|model|running'
      }
    }

    It 'maps the all-process query' {
      $Result = Get-OllamaProcess -All -ConfigDirectory 'C:\Config'

      $Result.Success | Should -BeTrue
      Should -Invoke Invoke-OllamactlJson -Times 1 -Exactly -ParameterFilter {
        ($ArgumentList -join '|') -eq '--output|json|--config-dir|C:\Config|process|list|--all'
      }
    }

    It 'honors WhatIf when starting the server' {
      $null = Start-OllamaServer -WhatIf

      Should -Invoke Invoke-OllamactlJson -Times 0 -Exactly
    }

    It 'resolves literal start files and maps server start options' {
      Mock Resolve-OllamactlFile {
        if ($Purpose -eq 'Ollama executable') { return 'C:\Resolved\ollama.exe' }
        return 'C:\Resolved\server.env'
      }

      $Result = Start-OllamaServer `
        -OllamaPath 'C:\Input\[ollama].exe' `
        -EnvironmentFile 'C:\Input\[server].env' `
        -Confirm:$false

      $Result.Success | Should -BeTrue
      Should -Invoke Resolve-OllamactlFile -Times 1 -Exactly -ParameterFilter {
        $LiteralPath -eq 'C:\Input\[ollama].exe' -and $Purpose -eq 'Ollama executable'
      }
      Should -Invoke Resolve-OllamactlFile -Times 1 -Exactly -ParameterFilter {
        $LiteralPath -eq 'C:\Input\[server].env' -and $Purpose -eq 'Ollama environment file'
      }
      Should -Invoke Invoke-OllamactlJson -Times 1 -Exactly -ParameterFilter {
        ($ArgumentList -join '|') -eq
          '--output|json|server|start|--ollama-path|C:\Resolved\ollama.exe|--env-file|C:\Resolved\server.env'
      }
    }

    It 'honors WhatIf and confirmation semantics when stopping the server' {
      $null = Stop-OllamaServer -WhatIf
      Should -Invoke Invoke-OllamactlJson -Times 0 -Exactly

      $Result = Stop-OllamaServer -ConfigDirectory 'C:\Config' -Confirm:$false
      $Result.Success | Should -BeTrue
      Should -Invoke Invoke-OllamactlJson -Times 1 -Exactly -ParameterFilter {
        ($ArgumentList -join '|') -eq '--output|json|--config-dir|C:\Config|server|stop'
      }
    }
  }
}

Describe 'Invoke-Ollamactl behavior' {
  InModuleScope Ollamactl.PowerShell {
    BeforeEach {
      Mock Invoke-OllamactlProcess {
        [pscustomobject]@{
          PSTypeName = 'Ollamactl.ProcessResult'
          ExecutablePath = 'C:\Tools\ollamactl.exe'
          Arguments = @($ArgumentList)
          ExitCode = 0
          StandardOutput = "0.3.0`r`n"
          StandardError = ''
        }
      }
    }

    It 'accepts remaining native-style arguments and writes stdout lines' {
      $Output = @(Invoke-Ollamactl --version)

      $Output | Should -Be @('0.3.0')
      Should -Invoke Invoke-OllamactlProcess -Times 1 -Exactly -ParameterFilter {
        $ArgumentList.Count -eq 1 -and $ArgumentList[0] -eq '--version'
      }
    }

    It 'returns the captured result with PassThru' {
      $Result = Invoke-Ollamactl -ArgumentList @('model', 'list') -PassThru

      $Result.PSObject.TypeNames | Should -Contain 'Ollamactl.ProcessResult'
      $Result.ExitCode | Should -Be 0
      $Result.Arguments | Should -Be @('model', 'list')
    }
  }
}
