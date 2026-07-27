#Requires -Version 7.2
#Requires -Modules Pester

$ModuleManifest = Join-Path $PSScriptRoot '..\..\src\Ollamactl.PowerShell\Ollamactl.PowerShell.psd1'
if (-not (Get-Module -Name Ollamactl.PowerShell)) {
  Import-Module $ModuleManifest
}

Describe 'Ollamactl.PowerShell private infrastructure' {
  InModuleScope Ollamactl.PowerShell {
    BeforeEach {
      $script:OriginalOllamactlExecutable = $env:OLLAMACTL_EXE
    }

    AfterEach {
      [Environment]::SetEnvironmentVariable(
        'OLLAMACTL_EXE',
        $script:OriginalOllamactlExecutable,
        'Process'
      )
    }

    It 'resolves an explicit literal path containing wildcard characters' {
      $LiteralExecutable = Join-Path $TestDrive '[ollamactl].exe'
      [System.IO.File]::WriteAllText($LiteralExecutable, 'test')

      Resolve-OllamactlExecutable -ExecutablePath $LiteralExecutable |
        Should -Be ([System.IO.Path]::GetFullPath($LiteralExecutable))
    }

    It 'uses OLLAMACTL_EXE before searching PATH' {
      $EnvironmentExecutable = Join-Path $TestDrive 'environment-ollamactl.exe'
      [System.IO.File]::WriteAllText($EnvironmentExecutable, 'test')
      $env:OLLAMACTL_EXE = $EnvironmentExecutable

      Resolve-OllamactlExecutable |
        Should -Be ([System.IO.Path]::GetFullPath($EnvironmentExecutable))
    }

    It 'throws a terminating error for a missing explicit executable' {
      $Missing = Join-Path $TestDrive '[missing].exe'

      { Resolve-OllamactlExecutable -ExecutablePath $Missing } |
        Should -Throw '*was not found*'
    }

    It 'executes a process without a shell and captures stdout' {
      $PowerShellExecutable = (Get-Process -Id $PID).Path
      $Result = Invoke-OllamactlProcess `
        -ExecutablePath $PowerShellExecutable `
        -ArgumentList @(
          '-NoLogo'
          '-NoProfile'
          '-NonInteractive'
          '-Command'
          "[Console]::Out.Write('captured')"
        )

      $Result.ExitCode | Should -Be 0
      $Result.StandardOutput | Should -Be 'captured'
      $Result.StandardError | Should -BeNullOrEmpty
      $Result.Arguments[-1] | Should -Be "[Console]::Out.Write('captured')"
    }

    It 'converts a nonzero process exit code into a terminating error' {
      $PowerShellExecutable = (Get-Process -Id $PID).Path

      {
        Invoke-OllamactlProcess `
          -ExecutablePath $PowerShellExecutable `
          -ArgumentList @(
            '-NoLogo'
            '-NoProfile'
            '-NonInteractive'
            '-Command'
            "[Console]::Error.Write('failure detail'); exit 7"
          )
      } | Should -Throw '*exited with code 7*failure detail*'
    }

    It 'can return a nonzero diagnostic result when explicitly requested' {
      $PowerShellExecutable = (Get-Process -Id $PID).Path

      $Result = Invoke-OllamactlProcess `
        -ExecutablePath $PowerShellExecutable `
        -AllowNonZeroExitCode `
        -ArgumentList @(
          '-NoLogo'
          '-NoProfile'
          '-NonInteractive'
          '-Command'
          "[Console]::Out.Write((@{ status = 'unhealthy' } | ConvertTo-Json -Compress)); exit 3"
        )

      $Result.ExitCode | Should -Be 3
      $Result.StandardOutput | Should -Be '{"status":"unhealthy"}'
    }

    It 'parses valid JSON returned by the process runner' {
      Mock Invoke-OllamactlProcess {
        [pscustomobject]@{
          StandardOutput = '{"status":"healthy","checks":3}'
          StandardError = ''
          ExitCode = 0
        }
      }

      $Result = Invoke-OllamactlJson -ArgumentList @('--output', 'json', 'health')

      $Result.status | Should -Be 'healthy'
      $Result.checks | Should -Be 3
    }

    It 'throws terminating errors for empty and invalid JSON' {
      Mock Invoke-OllamactlProcess {
        [pscustomobject]@{
          StandardOutput = ''
          StandardError = ''
          ExitCode = 0
        }
      }
      { Invoke-OllamactlJson -ArgumentList @('--output', 'json', 'health') } |
        Should -Throw '*empty response*'

      Mock Invoke-OllamactlProcess {
        [pscustomobject]@{
          StandardOutput = '{invalid'
          StandardError = ''
          ExitCode = 0
        }
      }
      { Invoke-OllamactlJson -ArgumentList @('--output', 'json', 'health') } |
        Should -Throw '*invalid JSON*'
    }
  }
}
