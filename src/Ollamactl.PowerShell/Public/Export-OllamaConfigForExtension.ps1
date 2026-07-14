function Export-OllamaConfigForExtension {

    [CmdletBinding()]
    param(
        [ValidateSet("Continue", "Cline")]
        [string]$Extension = "Continue",

        [ValidateSet("Global", "Project")]
        [string]$Scope = "Project",

        [string]$ProjectPath = (Get-Location).Path,

        [string]$Host,

        [string[]]$Models = @("qwen2.5-coder:7b", "qwen3:8b", "deepseek-r1:8b"),

        [string]$EmbedModel = "nomic-embed-text",

        [switch]$Force
    )

    if (-not $Host) {
        $Host = if ($env:OLLAMA_HOST) { $env:OLLAMA_HOST } else { "127.0.0.1:11434" }
    }
    $Base = if ($Host -match '^https?://') { $Host.TrimEnd("/") } else { "http://$Host" }

    switch ($Extension) {

        "Continue" {
            $Dir  = if ($Scope -eq "Global") {
                Join-Path $HOME ".continue"
            } else {
                Join-Path $ProjectPath ".continue"
            }
            $File = Join-Path $Dir "config.json"

            $ModelsJson = ($Models | ForEach-Object {
                "    { `"title`": `"$_`", `"provider`": `"ollama`", `"model`": `"$_`" }"
            }) -join ",`n"

            $Json = @"
{
  `"models`": [
$ModelsJson
  ],
  `"tabAutocompleteModel`": {
    `"title`": `"$($Models[0])`",
    `"provider`": `"ollama`",
    `"model`": `"$($Models[0])`"
  },
  `"embeddingsProvider`": {
    `"provider`": `"ollama`",
    `"model`": `"$EmbedModel`"
  },
  `"contextProviders`": [
    { `"name`": `"codebase`" },
    { `"name`": `"file`" },
    { `"name`": `"folder`" },
    { `"name`": `"git-diff`" }
  ]
}
"@

            if ((Test-Path $File) -and -not $Force) {
                throw "Config already exists: $File. Re-run with -Force to overwrite."
            }
            New-Item -ItemType Directory -Path $Dir -Force | Out-Null
            Set-Content -Path $File -Value $Json -NoNewline
            [pscustomobject]@{
                Extension = $Extension
                Scope     = $Scope
                Path      = $File
            }
        }

        "Cline" {
            $Dir  = Join-Path $HOME ".vscode"
            $File = Join-Path $Dir "settings.json"
            New-Item -ItemType Directory -Path $Dir -Force | Out-Null

            $PrimaryModel = $Models | Where-Object { $_ -match "qwen3|qwen2.5-coder" } | Select-Object -First 1
            if (-not $PrimaryModel) { $PrimaryModel = $Models[0] }

            $Settings = @{}
            if (Test-Path $File) {
                try { $Settings = Get-Content $File -Raw | ConvertFrom-Json -AsHashtable } catch { $Settings = @{} }
            }
            $Settings["cline.apiProvider"]    = "ollama"
            $Settings["cline.ollamaBaseUrl"]  = $Base
            $Settings["cline.ollamaModelId"]  = $PrimaryModel

            ($Settings | ConvertTo-Json -Depth 10) | Set-Content -Path $File -NoNewline
            [pscustomobject]@{
                Extension = $Extension
                Scope     = "Global"
                Path      = $File
            }
        }
    }
}