function Test-OllamaRunning {

    [CmdletBinding()]
    param()

    $Process = Get-Process `
        -Name "ollama" `
        -ErrorAction SilentlyContinue

    return ($null -ne $Process)
}
