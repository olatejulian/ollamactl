Set-StrictMode -Version Latest

$PrivateDirectory = Join-Path -Path $PSScriptRoot -ChildPath 'Private'
$PublicDirectory = Join-Path -Path $PSScriptRoot -ChildPath 'Public'

Get-ChildItem -LiteralPath $PrivateDirectory -Filter '*.ps1' -File |
  Sort-Object -Property Name |
  ForEach-Object { . $_.FullName }

$PublicFunctions = @(
  'Get-OllamaEndpoint'
  'Get-OllamaHealth'
  'Get-OllamaModel'
  'Get-OllamaProcess'
  'Invoke-Ollamactl'
  'Start-OllamaServer'
  'Stop-OllamaServer'
)

foreach ($FunctionName in $PublicFunctions) {
  $FunctionPath = Join-Path -Path $PublicDirectory -ChildPath "$FunctionName.ps1"
  . $FunctionPath
}

Export-ModuleMember -Function $PublicFunctions
