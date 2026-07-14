function Test-OllamaToolCalling {

    [CmdletBinding()]
    [OutputType([pscustomobject])]
    param(
        [Parameter(Mandatory)]
        [string]$Model,

        [string]$Host
    )

    if (-not $Host) {
        $Host = if ($env:OLLAMA_HOST) { $env:OLLAMA_HOST } else { "127.0.0.1:11434" }
    }
    $Base = if ($Host -match '^https?://') { $Host.TrimEnd("/") } else { "http://$Host" }

    $Body = @{
        model   = $Model
        stream  = $false
        tools   = @(
            @{
                type     = "function"
                function = @{
                    name        = "get_weather"
                    description = "Return the current weather for a city."
                    parameters  = @{
                        type       = "object"
                        properties = @{
                            city = @{
                                type        = "string"
                                description = "City name"
                            }
                        }
                        required = @("city")
                    }
                }
            }
        )
        messages = @(
            @{
                role    = "user"
                content = "What is the weather in Sao Paulo? Use the get_weather tool."
            }
        )
    } | ConvertTo-Json -Depth 12

    $Sw = [System.Diagnostics.Stopwatch]::StartNew()
    try {
        $Response = Invoke-RestMethod `
            -Uri "$Base/api/chat" `
            -Method POST `
            -ContentType "application/json" `
            -Body $Body `
            -TimeoutSec 120
        $Sw.Stop()

        $Called = $false
        $Args   = $null
        if ($Response.message.tool_calls) {
            $Call = $Response.message.tool_calls | Select-Object -First 1
            $Called = $true
            $Args   = $Call.function.arguments
        }

        [pscustomobject]@{
            Model         = $Model
            ElapsedMs     = [int]$Sw.ElapsedMilliseconds
            CalledTool    = $Called
            ToolName      = if ($Called) { $Call.function.name } else { $null }
            Arguments     = $Args
            RawResponse   = $Response
        }
    } catch {
        $Sw.Stop()
        [pscustomobject]@{
            Model       = $Model
            ElapsedMs   = [int]$Sw.ElapsedMilliseconds
            CalledTool  = $false
            Error       = $_.Exception.Message
        }
    }
}