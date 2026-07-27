# Wrapper routing

`ollamactl` is deliberately hybrid. The Ollama REST API is best for structured
and remote operations, the upstream CLI is best for complete feature coverage,
and local OS adapters are necessary for safe background lifecycle management.

## Routing table

| Command | Primary route | Local or remote | Notes |
|---|---|---|---|
| `ollama -- <args>` | Native `ollama.exe` | Local | Exact upstream command surface |
| `status` | REST API | Remote-capable | Version plus installed/running model views |
| `model list` | REST API | Remote-capable | Installed models |
| `model running` | REST API | Remote-capable | Loaded models and residency data |
| `model load` | REST API | Remote-capable | Requests model residency |
| `model unload` | REST API | Remote-capable | Requests immediate unload |
| `chat` | REST API | Remote-capable | One non-streaming chat request |
| `tools probe` | REST API | Remote-capable | Deterministic tool-call capability probe |
| `server start/stop/restart/status/logs` | Local process/filesystem | Local | Managed lifecycle and captured logs |
| `process list` | Local process/state | Local | Ownership-aware process inventory |
| `health` / `doctor` | Combined | Both | Config, executable, process, API, models, optional logs |
| `config show/env` | Local configuration | Local | Resolved and secret-safe metadata |
| `endpoint` | Local resolution | Local input | Normalized API endpoint and source |

## Why REST for structured operations?

The official Ollama API exposes programmatic model and inference operations at
`/api`. A local installation serves it at `http://localhost:11434/api` by
default. The [official API introduction] describes the base URL and stability
expectations.

REST gives `ollamactl`:

- predictable JSON independent of terminal formatting;
- direct cancellation and bounded timeouts;
- remote endpoint support;
- source-generated serialization contracts;
- deterministic fake-server integration and end-to-end tests.

An API command does not inspect local processes to decide whether a remote
server is healthy. This avoids incorrectly reporting a working remote endpoint
as unavailable merely because no local `ollama.exe` is running.

## Why native passthrough?

The upstream CLI includes model downloads/removal, interactive runs, model
creation, authentication, integrations, and features that can evolve faster
than this wrapper. The official [Ollama CLI reference] remains authoritative.

```powershell
ollamactl ollama -- pull qwen3:8b
ollamactl ollama -- run qwen3:8b
ollamactl ollama -- ps
```

Passthrough guarantees:

- for native executables, tokens after `--` are forwarded through
  `ProcessStartInfo.ArgumentList` without a command shell;
- explicitly selected Windows `.cmd` and `.bat` shims use `cmd.exe` with a
  dedicated, validated quoting path because Windows cannot execute batch files
  directly;
- stdout and stderr remain separate;
- the upstream exit code is returned;
- selecting `--ollama-path` does not change the current user's PATH.

Passthrough does not normalize upstream text into `ollamactl` JSON. Use a
dedicated REST-backed wrapper command when a stable structured contract is
required.

## Why local orchestration?

The REST API does not establish operating-system process ownership. Safe
background management requires local responsibilities:

- resolve an executable without shell lookup ambiguity;
- load a controlled child environment;
- start a detached supervisor/target process;
- capture stdout and stderr;
- persist process identity atomically;
- verify saved identity before termination;
- distinguish managed, external, exited, and stale processes.

This logic remains in Infrastructure behind Application ports. CLI handlers do
not call `Process.Kill` or write state directly.

## Combined health path

`health` / `doctor` is intentionally broader than `status`:

```text
configuration -> executable -> managed state -> live processes -> REST API -> models -> optional logs
```

A result can therefore distinguish cases such as:

- no local managed process, but a configured remote API is healthy;
- a managed process exists, but its API is not ready;
- the state file is stale and points to no matching process;
- the API is healthy, but no model is installed or loaded;
- the server exited and stderr contains bounded diagnostic evidence.

See [Health checks](health-check.md) for interpretation.

## Output ownership

For wrapper-owned commands, the CLI presentation layer owns text/JSON shape and
exit-code translation. Infrastructure supplies typed results and exceptions;
it does not write to the terminal.

For native passthrough, Ollama owns output shape and exit status. `ollamactl`
acts as a transparent, safely launched process boundary.

## Security boundary

Routing never grants more authority than the selected mechanism:

- a REST endpoint can perform API operations but cannot authorize local process
  termination;
- process discovery does not confer ownership;
- an env file supplies child-process data but is not executed;
- `--all` expands process visibility, not the stop target set;
- logs are opt-in for health output and must be treated as potentially
  sensitive.

[official API introduction]: https://docs.ollama.com/api/introduction
[Ollama CLI reference]: https://docs.ollama.com/cli
