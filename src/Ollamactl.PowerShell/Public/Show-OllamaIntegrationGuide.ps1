function Show-OllamaIntegrationGuide {

    [CmdletBinding()]
    param(
        [ValidateSet("Continue", "Cline", "Aider")]
        [string]$Extension = "Continue",

        [string]$Host
    )

    if (-not $Host) {
        $Host = if ($env:OLLAMA_HOST) { $env:OLLAMA_HOST } else { "127.0.0.1:11434" }
    }
    $Base = if ($Host -match '^https?://') { $Host.TrimEnd("/") } else { "http://$Host" }

    switch ($Extension) {

        "Continue" {
            @"
# Continue.dev (VS Code) - https://continue.dev
# Place the JSON at:  ~/.continue/config.json  (global)
#                     .continue/config.json     (per project)

{
  `"models`": [
    { `"title`": `"Qwen Coder 7B (Local)`",     `"provider`": `"ollama`", `"model`": `"qwen2.5-coder:7b`" },
    { `"title`": `"Qwen3 8B (Local, agent)`",   `"provider`": `"ollama`", `"model`": `"qwen3:8b`" },
    { `"title`": `"DeepSeek R1 8B (reasoning)`",`"provider`": `"ollama`", `"model`": `"deepseek-r1:8b`" }
  ],
  `"tabAutocompleteModel`": {
    `"title`": `"Qwen Coder 7B`", `"provider`": `"ollama`", `"model`": `"qwen2.5-coder:7b`"
  },
  `"embeddingsProvider`": {
    `"provider`": `"ollama`", `"model`": `"nomic-embed-text`"
  },
  `"contextProviders`": [
    { `"name`": `"codebase`" },
    { `"name`": `"file`" },
    { `"name`": `"folder`" },
    { `"name`": `"git-diff`" }
  ]
}

# Run in VS Code: `Continue: Open Config UI` or paste JSON above.
# Embedding model: `ollama pull nomic-embed-text` once.
"@
        }

        "Cline" {
            @"
# Cline (VS Code) - https://github.com/cline/cline
# Open Settings -> Cline -> API Provider = Ollama
#   Base URL: $Base
#   Model Id: qwen3:8b
# Cline reads these from VS Code settings; the JSON below shows the
# equivalent in settings.json.

{
  `"cline.apiProvider`": `"ollama`",
  `"cline.ollamaBaseUrl`": `"$Base`",
  `"cline.ollamaModelId`": `"qwen3:8b`"
}

# Cline works best with a tool-capable model. qwen3:8b advertises tools.
"@
        }

        "Aider" {
            @"
# Aider (terminal) - https://aider.chat
# Install: pip install aider-chat
# Set env so Aider talks to the local Ollama:

`$env:OLLAMA_API_BASE = `"$Base`"
aider --model ollama_chat/qwen2.5-coder:7b
# Or for the agent role:
aider --model ollama_chat/qwen3:8b

# Aider auto-commits each change. Use --no-auto-commits to review first.
"@
        }
    }
}