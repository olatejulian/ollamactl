# CLI reference

This reference documents the version 0.3.0 command contract. Generated help is
authoritative for the exact build in use:

```powershell
ollamactl --help
ollamactl <command> --help
```

## Syntax

```text
ollamactl [global-options] <command> [command-options] [arguments]
ollamactl [global-options] ollama -- <native-ollama-arguments>
```

Place global options before the command in automation. Use quotes appropriate
for the calling shell when an argument contains whitespace.

## Global options

| Option | Value | Applies to |
|---|---|---|
| `--host` | `host:port` or HTTP(S) origin without path/query/fragment | API commands and local server start/restart |
| `--timeout` | Integer from 1 to 600 seconds | HTTP and readiness operations |
| `--config-dir` | Directory path | Configuration, state, managed logs |
| `--env-file` | Existing dotenv path | Configuration plus managed/native child environment |
| `--ollama-path` | Existing executable path | Native passthrough, lifecycle start/restart, and health |
| `--output` | `text` or `json` | Structured wrapper commands |

Defaults and precedence are documented in [Configuration](configuration.md).
`--output` does not transform the output produced by `ollama.exe` in native
passthrough mode.

## Native Ollama passthrough

### `ollamactl ollama -- <args>`

Executes the selected Ollama CLI with the tokens following `--`.

```powershell
ollamactl ollama -- --version
ollamactl ollama -- pull qwen3:8b
ollamactl ollama -- run qwen3:8b 'Explain dependency inversion.'
ollamactl --ollama-path 'C:\Tools\Ollama\ollama.exe' ollama -- ps
```

The separator prevents Ollama flags from being parsed as wrapper flags. Native
executables are launched directly with an argument list. When an explicitly
selected Windows `.cmd` or `.bat` shim is used, `cmd.exe` is required and the
wrapper supplies a dedicated quoted command payload. Stdout, stderr, and exit
code come from the delegated process. Refer to the official [Ollama CLI
reference] for the upstream argument contract.

## API status

### `ollamactl status`

Checks the configured API and returns endpoint, Ollama version, installed
models, and running models.

```powershell
ollamactl status
ollamactl --host http://127.0.0.1:11434 --output json status
```

This is an API view, not proof that a local process is owned by `ollamactl`.
Use `server status`, `process list`, or `health` for local lifecycle evidence.

## Models

### `ollamactl model list`

Lists models installed on the configured Ollama server.

```powershell
ollamactl model list
ollamactl --output json model list
```

### `ollamactl model running`

Lists models currently loaded by the configured server, including resource and
expiration data supplied by Ollama.

```powershell
ollamactl model running
```

### `ollamactl model load <name> [--keep-alive <duration>]`

Loads a model and controls how long Ollama should keep it resident. The default
keep-alive is `30m`.

```powershell
ollamactl model load qwen3:8b --keep-alive 30m
```

The duration syntax is interpreted by Ollama. Loading can take longer on first
use or when model data is not already cached.

### `ollamactl model unload <name>`

Requests immediate unload of a running model.

```powershell
ollamactl model unload qwen3:8b
```

These commands manage residency, not installation. Use native passthrough for
upstream model installation/removal operations:

```powershell
ollamactl ollama -- pull qwen3:8b
ollamactl ollama -- rm qwen3:8b
```

Aliases: `ollamactl models` is equivalent to `ollamactl model`, and
`ollamactl model ps` is equivalent to `ollamactl model running`.

## Chat

### `ollamactl chat <model> <prompt>`

Sends one non-streaming chat request and prints the response.

```powershell
ollamactl chat qwen3:8b 'Return three PowerShell safety practices.'
ollamactl --output json chat qwen3:8b 'Return JSON-safe text.'
```

Quote multi-word prompts. For interactive or multimodal upstream behavior, use
native passthrough.

## Tool calling

### `ollamactl tools probe <model>`

Asks a model to invoke a deterministic test tool and reports whether a tool
call was produced.

```powershell
ollamactl tools probe qwen3:8b
ollamactl --output json tools probe qwen3:8b
```

A successful API request can still report that the model did not call the
tool; that result is meaningful for compatibility diagnostics and returns exit
code `1`.

## Managed server

### `ollamactl server start`

Starts `ollama serve` in the background with managed state and logs.

```powershell
ollamactl server start
ollamactl --env-file 'C:\Ollama\server.env' --ollama-path 'C:\Ollama\ollama.exe' server start
```

Start/restart accepts only a local endpoint. If a local Ollama API is already
responding, the command reports it as available and does not create a second
managed process or ownership state.

### `ollamactl server stop`

Stops only the identity-validated process tree recorded by `ollamactl`.

```powershell
ollamactl server stop
```

### `ollamactl server restart`

Stops the owned server when present, then starts it again with resolved current
configuration.

```powershell
ollamactl server restart
```

### `ollamactl server status`

Reports managed lifecycle state, identity, executable, state path, and log
paths. This is a local process view.

```powershell
ollamactl server status
ollamactl --output json server status
```

### `ollamactl server logs`

Reads the logs captured for the managed server under the selected configuration
directory and supported official Ollama `server.log` files when present.

```powershell
ollamactl server logs
ollamactl --output json server logs
```

See [Process management](process-management.md) and [Logging](logging.md).

## Processes

### `ollamactl process list`

Lists the local process information associated with the managed server.

```powershell
ollamactl process list
```

### `ollamactl process list --all`

Also includes discoverable Ollama-related processes that are not owned by the
selected `ollamactl` state.

```powershell
ollamactl process list --all
ollamactl --output json process list --all
```

Discovery is informational. An unrelated discovered process does not become
eligible for `server stop`.

Alias: `ollamactl processes` is equivalent to `ollamactl process`.

## Health and diagnosis

### `ollamactl health`

Runs the complete diagnosis without including log bodies.

```powershell
ollamactl health
ollamactl --output json health
```

### `ollamactl doctor`

Alias for `health`.

```powershell
ollamactl doctor
```

### Log evidence

```powershell
ollamactl health --include-logs
ollamactl health --include-logs --log-tail 200
```

`--log-tail` bounds the number of lines read from each supported managed or
official log. It is valid with `--include-logs`. See [Health
checks](health-check.md).

## Configuration

### `ollamactl config show`

Shows effective host, timeout, paths, and the source of resolved settings.

```powershell
ollamactl config show
ollamactl --output json config show
```

### `ollamactl config env`

Inspects the selected server-environment configuration with secret-safe output.
It does not dump arbitrary dotenv values.

```powershell
ollamactl config env
```

### `ollamactl endpoint`

Shows the normalized Ollama endpoint and its configuration source.

```powershell
ollamactl endpoint
ollamactl --host 192.0.2.10:11434 endpoint
```

## Output contract

Structured commands support:

- `--output text`: human-readable tables and summaries;
- `--output json`: machine-readable JSON on stdout.

Errors go to stderr. In JSON mode, error output is also machine-readable where
the wrapper owns the failure. Avoid parsing text tables in automation.

## Exit codes

| Code | Meaning |
|---:|---|
| `0` | Success |
| `1` | General operation failure |
| `2` | Invalid usage or configuration |
| `3` | Ollama/API unavailable |
| `130` | Operation cancelled |

Native passthrough returns the exit code from `ollama.exe`.

[Ollama CLI reference]: https://docs.ollama.com/cli
