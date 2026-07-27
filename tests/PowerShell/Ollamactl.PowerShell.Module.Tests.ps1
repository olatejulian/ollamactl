#Requires -Version 7.2
#Requires -Modules Pester

Describe 'Ollamactl.PowerShell module contract' {
  BeforeAll {
    $RepositoryRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..')).Path
    $ManifestPath = Join-Path $RepositoryRoot 'src\Ollamactl.PowerShell\Ollamactl.PowerShell.psd1'
    $ExpectedFunctions = @(
      'Get-OllamaEndpoint'
      'Get-OllamaHealth'
      'Get-OllamaModel'
      'Get-OllamaProcess'
      'Invoke-Ollamactl'
      'Start-OllamaServer'
      'Stop-OllamaServer'
    )

    if (-not (Get-Module -Name Ollamactl.PowerShell)) {
      Import-Module $ManifestPath
    }
  }

  It 'has a valid version 0.3.0 manifest with the expected metadata' {
    $Manifest = Test-ModuleManifest -Path $ManifestPath -ErrorAction Stop

    $Manifest.Version | Should -Be ([version]'0.3.0')
    $Manifest.CompatiblePSEditions | Should -Contain 'Core'
    $Manifest.ProjectUri.AbsoluteUri.TrimEnd('/') |
      Should -Be 'https://github.com/olatejulian/ollamactl'
    @($Manifest.ExportedFunctions.Keys | Sort-Object) |
      Should -Be @($ExpectedFunctions | Sort-Object)
  }

  It 'imports without output or operational side effects' {
    $EscapedManifest = $ManifestPath.Replace("'", "''")
    $Output = & pwsh -NoLogo -NoProfile -NonInteractive -Command @"
      `$ErrorActionPreference = 'Stop'
      Import-Module '$EscapedManifest' -Force
      'IMPORTED'
"@

    $LASTEXITCODE | Should -Be 0
    $Output | Should -Be @('IMPORTED')
  }

  It 'exports exactly one function per public file' {
    $PublicDirectory = Join-Path (Split-Path -Parent $ManifestPath) 'Public'
    $PublicFiles = @(
      Get-ChildItem -LiteralPath $PublicDirectory -Filter '*.ps1' -File |
        ForEach-Object BaseName |
        Sort-Object
    )
    $ExportedFunctions = @(
      Get-Command -Module Ollamactl.PowerShell |
        ForEach-Object Name |
        Sort-Object
    )

    $PublicFiles | Should -Be @($ExpectedFunctions | Sort-Object)
    $ExportedFunctions | Should -Be @($ExpectedFunctions | Sort-Object)
  }

  It 'provides detailed help and examples for every exported command' {
    foreach ($CommandName in $ExpectedFunctions) {
      $Help = Get-Help -Name $CommandName -Full

      $Help.Synopsis | Should -Not -BeNullOrEmpty -Because $CommandName
      $Help.Description.Text | Should -Not -BeNullOrEmpty -Because $CommandName
      @($Help.Examples.Example).Count | Should -BeGreaterThan 0 -Because $CommandName
      @(
        $Help.Parameters.Parameter | Where-Object {
          $DescriptionProperty = $_.PSObject.Properties['Description']
          $null -ne $DescriptionProperty -and $null -ne $DescriptionProperty.Value
        }
      ).Count |
        Should -BeGreaterThan 0 -Because $CommandName
    }
  }

  It 'enables ShouldProcess for both state-changing commands' {
    (Get-Command Start-OllamaServer).Parameters.ContainsKey('WhatIf') | Should -BeTrue
    (Get-Command Start-OllamaServer).Parameters.ContainsKey('Confirm') | Should -BeTrue
    (Get-Command Stop-OllamaServer).Parameters.ContainsKey('WhatIf') | Should -BeTrue
    (Get-Command Stop-OllamaServer).Parameters.ContainsKey('Confirm') | Should -BeTrue
  }

  It 'does not retain executable legacy commands in the module source' {
    foreach ($Name in @(
        'Get-OllamaStatus.ps1'
        'Invoke-OllamaModel.ps1'
        'Start-Ollama.ps1'
        'Stop-Ollama.ps1'
      )) {
      Test-Path -LiteralPath (Join-Path $RepositoryRoot "src\Ollamactl.PowerShell\Public\$Name") |
        Should -BeFalse
    }
  }
}
