@{
  Run = @{
    Path = @('./tests/PowerShell')
    Exit = $false
    PassThru = $true
  }
  Output = @{
    Verbosity = 'Detailed'
  }
  TestResult = @{
    Enabled = $true
    OutputFormat = 'NUnitXml'
    OutputPath = './TestResults/pester-tests.xml'
  }
  CodeCoverage = @{
    Enabled = $true
    Path = @(
      './src/Ollamactl.PowerShell/Private/*.ps1'
      './src/Ollamactl.PowerShell/Public/*.ps1'
    )
    OutputFormat = 'JaCoCo'
    OutputPath = './TestResults/pester-coverage.xml'
  }
}
