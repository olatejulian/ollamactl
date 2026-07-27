# ollamactl

`ollamactl` is a modern, script-friendly Windows CLI for operating Ollama. It
combines the official Ollama REST API, safe delegation to `ollama.exe`, and
local process supervision behind one stable command surface and one
self-contained `ollamactl.exe`.

The project targets automation as well as interactive use: human-readable text
is the default, deterministic JSON is available for scripts, results go to
stdout, diagnostics go to stderr, and exit codes are stable.

> Version 0.3.0 is the current development version. See the
> [changelog](CHANGELOG.md) and [release guide](docs/release.md). This repository
> does not currently declare a software license; see [License](#license).

## Why ollamactl?

- Use one executable for Ollama API calls, native CLI commands, server
  lifecycle, process inspection, health checks, configuration, and logs.
- Forward any official Ollama CLI command without waiting for a dedicated
  wrapper command.
- Start `ollama serve` in the background with a controlled environment file and
  persistent stdout/stderr logs.
- Stop only the process tree started and identity-validated by `ollamactl`.
- Inspect installed and running models through the REST API, including remote
  Ollama endpoints.
- Run a combined diagnosis across configuration, executable discovery,
  processes, the API, models, and optionally bounded log excerpts.
- Emit `--output json` for PowerShell, CI, monitoring, and other automation.
- Ship as a self-contained Windows executable; the target machine does not need
  a separate .NET runtime.

`ollamactl` does **not** bundle Ollama, GPU drivers, or model data. Install
Ollama separately and follow the official [Ollama CLI reference],
[API introduction], and [troubleshooting guide] when working with the upstream
runtime.

## Requirements

For normal use:

- Windows and an `ollamactl.exe` build for `win-x64` or `win-arm64`;
- Ollama installed, or a direct path supplied with `--ollama-path`;
- enough CPU/GPU memory and disk space for the selected models.

The official Ollama Windows documentation currently lists Windows 10 22H2 or
newer and describes supported GPU drivers. Check the current [Ollama Windows
requirements] before installation.

For development:

- .NET SDK 10.0.302 or a compatible patch selected by `global.json`;
- PowerShell 7.3 or newer;
- Pester 5 or newer for the PowerShell suite;
- PSScriptAnalyzer for the PowerShell static-analysis gate.

## Installation

### GitHub release

Download the ZIP matching your Windows runtime from this repository's Releases
page, extract it, and verify the included checksum before placing
`ollamactl.exe` on `PATH`.

```powershell
Expand-Archive .\ollamactl-0.3.0-win-x64-singlefile.zip -DestinationPath .\ollamactl
Set-Location .\ollamactl

$expected = ((Get-Content -LiteralPath .\ollamactl.exe.sha256) -split '\s+')[0]
$actual = (Get-FileHash -LiteralPath .\ollamactl.exe -Algorithm SHA256).Hash.ToLowerInvariant()
if ($actual -ne $expected) { throw 'ollamactl.exe checksum mismatch.' }

.\ollamactl.exe --version
```

Release assets include an executable, `ollamactl.exe.sha256`, an artifact
manifest, and a versioned ZIP. Code-signing status is release-specific; do not
assume a binary is signed unless the release notes say so.

### Local build artifact

From a source checkout:

```powershell
.\build.ps1
.\artifacts\win-x64\ollamactl.exe --version
```

Use `-Runtime win-arm64` for ARM64 or `-NativeAot` for the optional Native AOT
publish mode. See [Development](docs/development.md) and
[Releasing](docs/release.md) for the complete pipeline.

## Quick start

The following flow uses a managed background server, checks it, lists models,
and sends a prompt:

```powershell
ollamactl server start
ollamactl health
ollamactl model list
ollamactl chat qwen3:8b 'Give me a one-line summary of Clean Architecture.'
```

To use an existing Ollama server, omit `server start` and select its endpoint:

```powershell
ollamactl --host http://127.0.0.1:11434 status
ollamactl --host http://127.0.0.1:11434 --output json model running
```

To call an upstream command directly, place its arguments after `--`:

```powershell
ollamactl ollama -- pull qwen3:8b
ollamactl ollama -- ps
```

The argument separator is intentional: everything after `--` belongs to
`ollama.exe`, not to `ollamactl`.

## Hybrid wrapper model

`ollamactl` selects the narrowest reliable integration for each job:

| Route | Used for | Benefit |
|---|---|---|
| Ollama REST API | Status, models, chat, tool probing | Structured, remote-capable, deterministic JSON |
| Native `ollama.exe` | `ollamactl ollama -- <args>` | Immediate access to the complete upstream CLI |
| Local orchestration | Server lifecycle, process ownership, config, logs | Safe background management and diagnosis |
| Combined diagnosis | `health` / `doctor` | Correlates local state with API reachability |

REST commands can target a remote server and do not require a local Ollama
process. Process and server commands describe the local machine. Read
[Wrapper routing](docs/wrapper-routing.md) for the exact boundary.

## Command overview

```text
ollamactl ollama -- <args>
ollamactl status
ollamactl model list
ollamactl model running
ollamactl model load <name> [--keep-alive <duration>]
ollamactl model unload <name>
ollamactl chat <model> <prompt>
ollamactl tools probe <model>
ollamactl server start
ollamactl server stop
ollamactl server restart
ollamactl server status
ollamactl server logs
ollamactl process list [--all]
ollamactl health [--include-logs] [--log-tail <lines>]
ollamactl doctor [--include-logs] [--log-tail <lines>]
ollamactl config show
ollamactl config env
ollamactl endpoint
```

Use `ollamactl <command> --help` for generated help and see the full
[CLI reference](docs/cli-reference.md).

### Global options

| Option | Purpose |
|---|---|
| `--host <host:port\|url>` | Select the API endpoint and managed-server bind address |
| `--timeout <1-600>` | Set the bounded HTTP/readiness timeout in seconds |
| `--config-dir <path>` | Override the default `~/.config/ollama` directory |
| `--env-file <path>` | Select dotenv configuration and the native child environment |
| `--ollama-path <path>` | Select the native executable for passthrough, lifecycle, and health checks |
| `--output <text\|json>` | Choose human-readable text or machine-readable JSON |

Place global options before the command in scripts for clarity. An HTTP(S)
`--host` value must be an origin without a path, query, or fragment. `--env-file`
can supply host and timeout defaults and is inherited by native child processes.
`server start` and `server restart` accept only local host endpoints.

### Exit codes

| Code | Meaning |
|---:|---|
| `0` | Success |
| `1` | Operation failed |
| `2` | Invalid usage or configuration |
| `3` | Ollama or a required endpoint is unavailable |
| `130` | Cancelled |

Native passthrough preserves the delegated command's stdout, stderr, and exit
code so existing Ollama automation remains observable.

## Configuration and environment

The default configuration directory is `~/.config/ollama` and its default
dotenv file is `~/.config/ollama/.env`. Effective values follow this precedence:

1. command-line option;
2. current process environment;
3. selected dotenv file;
4. built-in default.

Start from the checked-in template:

```powershell
New-Item -ItemType Directory -Path ~/.config/ollama -Force | Out-Null
Copy-Item .\.env.example ~/.config/ollama/.env
ollamactl config show
ollamactl config env
```

Common values include `OLLAMA_HOST`, `OLLAMACTL_TIMEOUT_SECONDS`,
`OLLAMA_MODELS`, `OLLAMA_KEEP_ALIVE`, and `OLLAMA_DEBUG`. The environment file
is loaded into a managed server process; it is never evaluated as a shell
script. Configuration output reports effective metadata and sources without
dumping secret values.

Do not commit `.env` files or tokens. Protect the configuration directory with
user-only filesystem permissions, and remember that a locally exposed Ollama
API does not require authentication by default. See
[Configuration](docs/configuration.md) for precedence, validation, and remote
endpoint guidance.

## Managed server lifecycle

```powershell
ollamactl server start
ollamactl server status
ollamactl process list
ollamactl server restart
ollamactl server stop
```

`server start` resolves `ollama.exe`, loads the selected environment, launches
`ollama serve` in the background, redirects its output, and records identity
metadata under the selected configuration directory. `server stop` validates
that identity before terminating the owned process tree. It does not kill every
process named `ollama` and does not claim ownership of a server started by the
Ollama desktop app or another service.

Multiple `--config-dir` values create independent state and log locations. See
[Process management](docs/process-management.md) before automating multiple
instances.

## Health checks and logs

Run a normal diagnosis first:

```powershell
ollamactl health
```

Add bounded log evidence only when needed:

```powershell
ollamactl health --include-logs --log-tail 200
ollamactl server logs
```

Managed server logs are stored by default at:

```text
~/.config/ollama/logs/ollama.out.log
~/.config/ollama/logs/ollama.err.log
```

Ollama's desktop application has its own upstream log locations. On Windows,
the official documentation points to `%LOCALAPPDATA%\Ollama`. See
[Health checks](docs/health-check.md), [Logging](docs/logging.md), and
[Troubleshooting](docs/troubleshooting.md).

## PowerShell module

The optional `Ollamactl.PowerShell` module is a thin wrapper over
`ollamactl.exe`; it does not duplicate process or HTTP policy. Structured
commands request JSON and return PowerShell objects.

```powershell
Import-Module .\src\Ollamactl.PowerShell\Ollamactl.PowerShell.psd1
Get-OllamaHealth -IncludeLogs -LogTail 100
Get-OllamaModel -Running
Start-OllamaServer -WhatIf
```

`Get-OllamaHealth` returns the structured report even when the CLI health exit
code denotes a degraded or unhealthy state; inspect its `status` and `checks`
properties in PowerShell automation.

Run `Get-Command -Module Ollamactl.PowerShell` and `Get-Help <command> -Full`
for the module contract.

## Build and test

Run the full build from PowerShell 7.3 or newer:

```powershell
.\build.ps1
```

Run validation without packaging:

```powershell
.\scripts\Test.ps1
```

The pipeline restores, verifies formatting, builds with warnings as errors,
runs unit/integration/end-to-end/architecture tests, runs Pester and
PSScriptAnalyzer when installed, publishes into a staging directory, exercises
the staged executable, and only then promotes release artifacts.

## Documentation

- [Architecture](docs/architecture.md)
- [CLI reference](docs/cli-reference.md)
- [Configuration](docs/configuration.md)
- [Wrapper routing](docs/wrapper-routing.md)
- [Process management](docs/process-management.md)
- [Health checks](docs/health-check.md)
- [Logging](docs/logging.md)
- [Troubleshooting](docs/troubleshooting.md)
- [Development](docs/development.md)
- [Release process](docs/release.md)
- [Contributing](CONTRIBUTING.md)
- [Security policy](SECURITY.md)
- [Changelog](CHANGELOG.md)

## Contributing and security

Read [CONTRIBUTING.md](CONTRIBUTING.md) before opening a change. Report
suspected vulnerabilities through the private path described in
[SECURITY.md](SECURITY.md), not through a public issue.

## License

No software license has been selected for this repository. The absence of a
license means no permission to use, copy, modify, or distribute the code is
granted beyond rights provided by applicable law. The project owner must add an
explicit license before the project can be treated as open source.

[Ollama CLI reference]: https://docs.ollama.com/cli
[API introduction]: https://docs.ollama.com/api/introduction
[troubleshooting guide]: https://docs.ollama.com/troubleshooting
[Ollama Windows requirements]: https://docs.ollama.com/windows
