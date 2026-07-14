# Root module for Ollamactl.PowerShell.
# Dot-sources Private helpers then Public cmdlets. Order matters because
# Public cmdlets depend on Private helpers.

$ModuleDir = $PSScriptRoot

$privateDir = Join-Path $ModuleDir "Private"
if (Test-Path $privateDir) {
  Get-ChildItem -Path $privateDir -Filter "*.ps1" -File |
    ForEach-Object { . $_.FullName }
}

$publicDir = Join-Path $ModuleDir "Public"
if (Test-Path $publicDir) {
  Get-ChildItem -Path $publicDir -Filter "*.ps1" -File |
    ForEach-Object { . $_.FullName }
}
