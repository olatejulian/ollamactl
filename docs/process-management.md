# Process management

`ollamactl` can supervise a local `ollama serve` process without treating every
process named `ollama` as its own. Ownership is explicit, durable, and verified
before termination.

## Scope

Process management is local to the machine running `ollamactl.exe`.

- `--host` may point REST commands at a remote API.
- `server` and `process` commands still operate only on the local OS.
- A server started by the Ollama desktop application, a service manager, a
  container, or another terminal is external unless `ollamactl` started and
  recorded it.

## Files

For the default configuration directory:

```text
~/.config/ollama/run/ollamactl-server.json
~/.config/ollama/logs/ollama.out.log
~/.config/ollama/logs/ollama.err.log
```

Use `ollamactl config show` or `ollamactl server status` rather than assuming a
path. `--config-dir` relocates state and managed logs together.

## Lifecycle states

The managed status distinguishes these concepts:

| State | Meaning |
|---|---|
| Not managed | No usable state exists for the selected config directory |
| Starting | A supervisor is recorded and the target is becoming ready |
| Running | Saved identity matches live managed processes |
| Exited | The recorded managed process completed |
| Stale | State exists but no live process identity matches it |

API health is a separate dimension. A process can be running while its API is
not ready, and an API can be healthy even when it belongs to an external or
remote process.

## Start

```powershell
ollamactl server start
```

The start workflow:

1. resolves the selected configuration directory;
2. resolves `ollama.exe` from `--ollama-path`, `OLLAMA_EXE`, or PATH;
3. parses the selected dotenv file as data;
4. checks whether the configured local API is already responding;
5. checks whether a matching managed server already exists;
6. creates state and log directories;
7. launches a background supervisor and `ollama serve` with separate arguments;
8. redirects target stdout and stderr to managed logs;
9. writes process identity atomically;
10. waits only within the configured bound for startup/readiness evidence.

Starting an already-running matching managed server is idempotent: it reports
the existing state rather than creating an untracked duplicate.
If any Ollama server is already responding at the selected local endpoint, the
start is also a safe no-op and no ownership state is claimed for that external
process. Remote endpoints are rejected for local start/restart.

Select files explicitly when required:

```powershell
ollamactl `
  --config-dir C:\Ollama\instance-a `
  --env-file C:\Ollama\instance-a.env `
  --ollama-path C:\Ollama\ollama.exe `
  server start
```

## Identity validation

PID alone is not ownership proof because operating systems reuse PIDs. Managed
state records enough identity to compare saved and live processes, including:

- supervisor and target process identifiers where available;
- process start identity/timestamps;
- supervisor and target executable paths;
- lifecycle/exit information;
- state and log locations.

Before a stop operation, `ollamactl` validates the live process against saved
identity. A missing process, reused PID, executable mismatch, corrupt state, or
inaccessible identity becomes stale/not-managed evidence. It does not trigger a
name-based kill.

The state file must not contain the entire child environment or dotenv values.

## Status

```powershell
ollamactl server status
ollamactl --output json server status
```

Use status to inspect ownership and lifecycle without requiring an API call.
Use `ollamactl status` for the API view and `ollamactl health` to correlate both.

## Process inventory

```powershell
ollamactl process list
ollamactl process list --all
```

The default view focuses on the selected managed server. `--all` includes other
discoverable Ollama-related processes for diagnosis. Those extra entries remain
external and are never implicitly adopted.

Process information can change immediately after it is read. Automation should
treat it as a snapshot, not as a lock or authorization token.

## Stop

```powershell
ollamactl server stop
```

Stop follows this safety order:

1. read state for the selected config directory;
2. locate the recorded process;
3. compare live identity to saved identity;
4. terminate only a confirmed owned process tree;
5. wait for completion within the operation's cancellation boundary;
6. remove or update state.

If state is stale, `ollamactl` cleans/reports stale state without terminating an
unrelated process. If no managed state exists, use `process list --all` to
understand an external server; stop it with the tool that owns it.

## Restart

```powershell
ollamactl server restart
```

Restart composes the safe stop and start workflows. Current command-line,
process-environment, dotenv, and path configuration is resolved for the new
start, so review `config show` and `config env` before using restart after a
configuration change.

## Multiple instances

Use a separate config directory and `OLLAMA_HOST` binding for every instance:

```powershell
ollamactl --config-dir C:\Ollama\a --env-file C:\Ollama\a.env server start
ollamactl --config-dir C:\Ollama\b --env-file C:\Ollama\b.env server start
```

Repeat the same `--config-dir` on status, logs, restart, process, and stop
commands. Separate state directories do not resolve port conflicts; the env
files must configure distinct listen addresses.

## External Ollama processes

`ollamactl` intentionally does not stop:

- the Ollama Windows tray application;
- a Windows service or scheduled task;
- a process started by another user or config root;
- a containerized or remote server;
- a process that merely has an Ollama-like name.

Use the relevant owner (desktop app, service manager, container runtime, or
remote administrator) for those processes.

## Recovery

When lifecycle behavior is unclear:

```powershell
ollamactl server status
ollamactl process list --all
ollamactl health --include-logs --log-tail 200
```

Do not manually edit the state file while a managed server is active. If status
reports stale state, allow the normal `server stop` or subsequent `server start`
workflow to reconcile it. Preserve the state and logs first when investigating
a suspected defect.

## PowerShell safety

The module exposes `Start-OllamaServer` and `Stop-OllamaServer` with
`SupportsShouldProcess`:

```powershell
Start-OllamaServer -WhatIf
Stop-OllamaServer -Confirm
```

These functions delegate the actual identity policy to `ollamactl.exe`; they do
not use `Stop-Process -Name ollama`.
