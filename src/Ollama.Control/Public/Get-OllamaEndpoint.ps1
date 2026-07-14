function Get-OllamaEndpoint {

    [CmdletBinding()]
    [OutputType([pscustomobject])]
    param(
        [string]$Host
    )

    if (-not $Host) {
        $Host = if ($env:OLLAMA_HOST) { $env:OLLAMA_HOST } else { "127.0.0.1:11434" }
    }

    # Accept either "host:port" or full URL.
    $Base = if ($Host -match '^https?://') {
        $Host.TrimEnd("/")
    } else {
        "http://$Host"
    }

    $Tags = Invoke-RestMethod -Uri "$Base/api/tags" -TimeoutSec 5
    $Ps   = Invoke-RestMethod -Uri "$Base/api/ps"   -TimeoutSec 5
    $Show = Invoke-RestMethod -Uri "$Base/api/version" -TimeoutSec 5

    $Models = @()
    foreach ($m in $Tags.models) {
        $Detail = $null
        try {
            $Detail = Invoke-RestMethod `
                -Uri "$Base/api/show" `
                -Method POST `
                -ContentType "application/json" `
                -Body (@{ name = $m.name } | ConvertTo-Json) `
                -TimeoutSec 10
        } catch {
            $Detail = $null
        }

        $Capabilities = @()
        if ($Detail -and $Detail.capabilities) {
            $Capabilities = @($Detail.capabilities)
        }

        $Models += [pscustomobject]@{
            Name              = $m.name
            SizeBytes         = $m.size
            ParameterSize     = $m.details.parameter_size
            Quantization      = $m.details.quantization_level
            Family            = $m.details.family
            ContextLength     = $m.details.context_length
            SupportsTools     = $Capabilities -contains "tools"
            SupportsThinking  = $Capabilities -contains "thinking"
            SupportsInsert    = $Capabilities -contains "insert"
        }
    }

    $Loaded = @($Ps.models | ForEach-Object {
        [pscustomobject]@{
            Name      = $_.name
            SizeBytes = $_.size
            SizeVRAM  = $_.size_vram
            ExpiresAt = $_.expires_at
        }
    })

    [pscustomobject]@{
        BaseUrl         = $Base
        OpenAiCompatUrl = "$Base/v1"
        Version         = $Show.version
        Models          = $Models
        Loaded          = $Loaded
        ToolCapable     = @($Models | Where-Object { $_.SupportsTools }).Name
        ChatCapable     = @($Models | Where-Object { $_.Capabilities ? $_.Capabilities.Contains("completion") : $true }).Name
    }
}