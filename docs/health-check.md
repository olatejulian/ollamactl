# Health checks

`ollamactl health` (alias `doctor`) correlates configuration, local process
state, API reachability, model inventory, and optional log evidence. It is the
preferred first command when the failure domain is not yet known.

## Basic use

```powershell
ollamactl health
ollamactl --output json health
```

The default report does not include log bodies. This keeps routine checks fast
and reduces accidental disclosure.

## Include bounded logs

```powershell
ollamactl health --include-logs
ollamactl health --include-logs --log-tail 200
```

`--log-tail` limits each supported managed or official log excerpt and is valid
with `--include-logs`. Start with a small bound and increase it only when the
first report lacks the relevant event.

## Diagnostic areas

The complete diagnosis is designed to cover:

| Area | Question answered |
|---|---|
| Configuration | Which endpoint is effective, and which sources supplied host and timeout? |
| Executable | Can the selected `ollama.exe` be resolved and executed? |
| Managed state | Is server state absent, starting, running, exited, or stale? |
| Processes | Do saved identities match live supervisor/target processes? |
| API | Is the configured HTTP endpoint reachable within the timeout? |
| Version | Does the endpoint return a valid Ollama version response? |
| Models | Which models are installed and currently loaded? |
| Logs | What bounded managed or supported official evidence is available when requested? |

Not every area applies equally to a remote endpoint. A healthy remote API does
not need a local managed process. The report should preserve that distinction
rather than treating local absence as remote failure.

## Related commands

Use narrower commands after health identifies the subsystem:

```powershell
ollamactl config show
ollamactl config env
ollamactl endpoint
ollamactl server status
ollamactl process list --all
ollamactl status
ollamactl model list
ollamactl model running
ollamactl server logs
```

## Interpreting common combinations

### API healthy, no managed process

Likely explanations:

- the Ollama desktop application owns the local server;
- a service/container owns it;
- `--host` points to a remote machine.

This is not automatically an error. `server stop` will not terminate that
external process.

### Managed process running, API unavailable

Possible causes include startup still in progress, a bind failure, an incorrect
`OLLAMA_HOST`, a port conflict, proxy settings, or an early runtime/GPU error.

Check:

```powershell
ollamactl server status
ollamactl server logs
ollamactl endpoint
```

### State stale

The recorded identity no longer matches a live process. This can occur after a
crash, forced termination, reboot, or external state-file modification.
`server stop` must not kill a mismatched PID. Let the managed lifecycle
reconcile stale state and preserve logs if the exit cause matters.

### API healthy, no installed models

Install one through native passthrough:

```powershell
ollamactl ollama -- pull qwen3:8b
```

Then run `model list` again.

### Model installed, not running

Load it explicitly or issue an inference request:

```powershell
ollamactl model load qwen3:8b --keep-alive 30m
ollamactl model running
```

### Server exited with stderr evidence

Review the bounded excerpt, then the full managed log if appropriate. Upstream
GPU discovery, permissions, driver, model storage, and network-bind errors are
documented in the official [Ollama troubleshooting guide].

## Automation

Use JSON and the process exit code rather than parsing text:

```powershell
$report = ollamactl --output json health | ConvertFrom-Json
if ($LASTEXITCODE -ne 0) {
  throw "Ollama health failed with exit code $LASTEXITCODE"
}
$report
```

Do not assume that JSON field order is significant. Consumers should tolerate
new additive diagnostic fields within the 0.3 line while relying on documented
names and exit semantics.

For periodic monitoring, avoid `--include-logs` unless evidence is required;
log content is larger and more sensitive than health metadata.

## Timeouts and cancellation

Use `--timeout` to bound API/readiness work:

```powershell
ollamactl --timeout 10 health
```

Timeout is not a model-performance benchmark. A first model load can take much
longer than a simple API health request. Cancellation returns exit code `130`.

## Privacy and redaction

Health reports should contain enough evidence to locate a fault without copying
the full environment. In particular:

- dotenv values are not dumped;
- process state does not store the child environment;
- log inclusion is explicit and line-bounded;
- paths, model names, prompts, and upstream log messages can still be sensitive;
- users must review JSON/logs before attaching them to an issue.

If a secret appears in upstream output, redact it and rotate it before sharing.

[Ollama troubleshooting guide]: https://docs.ollama.com/troubleshooting
