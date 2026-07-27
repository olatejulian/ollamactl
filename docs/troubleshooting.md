# Troubleshooting

Start with the combined diagnosis and then narrow the subsystem:

```powershell
ollamactl health
ollamactl config show
ollamactl server status
ollamactl process list --all
```

Add logs only when the first report points to process startup or runtime output:

```powershell
ollamactl health --include-logs --log-tail 200
```

The official [Ollama troubleshooting guide] is authoritative for upstream GPU,
driver, service, and runtime problems.

## `ollamactl.exe` is not found

Symptoms:

- PowerShell reports that `ollamactl` is not recognized;
- the PowerShell module reports executable discovery failure.

Checks:

```powershell
Get-Command ollamactl.exe -CommandType Application
Test-Path -LiteralPath .\artifacts\win-x64\ollamactl.exe
```

Use the full path, add its directory to PATH, or set `OLLAMACTL_EXE` for the
PowerShell module. `OLLAMACTL_EXE` selects the wrapper; `OLLAMA_EXE` selects the
upstream Ollama executable.

## `ollama.exe` is not found

Check upstream discovery:

```powershell
Get-Command ollama.exe -CommandType Application
ollamactl --ollama-path 'C:\Program Files\Ollama\ollama.exe' ollama -- --version
```

If Ollama is not installed, use the official [Ollama Windows guide]. The
`ollamactl` artifact does not bundle it.

## API unavailable (exit code 3)

```powershell
ollamactl endpoint
ollamactl server status
ollamactl process list --all
ollamactl --timeout 10 status
```

Verify:

- the normalized host and port;
- whether the server is local, remote, desktop-owned, or managed;
- firewall/proxy rules;
- whether another process is bound to the configured port;
- whether `OLLAMA_HOST` differs between the client process and managed env file.

The official API default is `http://localhost:11434/api`. Remote exposure
requires deliberate network security; local API access has no authentication by
default.

## Managed server exits during startup

```powershell
ollamactl server status
ollamactl server logs
ollamactl health --include-logs --log-tail 200
```

Common causes:

- invalid executable or env-file path;
- malformed dotenv input;
- address already in use;
- inaccessible model/log directory;
- unsupported GPU driver or runtime library;
- insufficient memory or disk space.

For upstream hardware/runtime failures, compare stderr with the official
[Ollama troubleshooting guide].

## State is stale

Stale means saved process identity does not match a live process. It can follow
a reboot, crash, forced kill, or PID reuse.

```powershell
ollamactl server status
ollamactl process list --all
ollamactl server stop
```

The safe stop workflow may reconcile stale state, but it must not terminate a
mismatched process. Do not edit the state file to make an unrelated PID appear
owned.

## `server stop` does not stop an Ollama process

This is expected when that process was started by the desktop app, a service,
container, another config directory, or another user.

```powershell
ollamactl process list --all
```

Stop external processes with their actual owner. `--all` changes visibility,
not termination authority.

## Model is not listed

Confirm the endpoint, then ask the upstream CLI to install the model:

```powershell
ollamactl endpoint
ollamactl model list
ollamactl ollama -- pull qwen3:8b
ollamactl model list
```

Native passthrough runs locally. If `--host` selects a remote API, ensure the
model is installed on that remote server rather than only on the local machine.

## Model load is slow or fails

```powershell
ollamactl model load qwen3:8b --keep-alive 30m
ollamactl model running
ollamactl health --include-logs --log-tail 200
```

First load may involve disk reads and memory allocation. Check available RAM,
VRAM, model size, model storage permissions, and upstream GPU diagnostics. The
wrapper HTTP timeout and Ollama's own load timeout are separate settings.

## Tool probe returns no tool call

The request can succeed even when a model declines or cannot produce a tool
call.

```powershell
ollamactl model list
ollamactl tools probe qwen3:8b
```

Confirm that the selected model advertises/implements tool capability and try a
current model version. A no-call result is a compatibility result, not
necessarily an unavailable server.

## Environment changes are not applied

```powershell
ollamactl config show
ollamactl config env
ollamactl server restart
```

Remember the precedence order: command line, process environment, dotenv,
default. A process environment value can override the file. Environment changes
affect a new child process; restart is required for an already-running server.

Use the same `--config-dir` and `--env-file` across inspection and lifecycle
commands.

## JSON cannot be parsed

Capture streams separately and check the exit code:

```powershell
$json = ollamactl --output json health 2> .\ollamactl-error.txt
if ($LASTEXITCODE -ne 0) {
  Get-Content -LiteralPath .\ollamactl-error.txt
  throw "ollamactl failed: $LASTEXITCODE"
}
$json | ConvertFrom-Json
```

Do not merge stderr into stdout before parsing. Native passthrough output is
owned by `ollama.exe` and is not converted into the wrapper JSON schema.

## Logs are missing

```powershell
ollamactl config show
ollamactl server status
ollamactl server logs
```

Managed logs exist only for a server started under that config directory. For
the Windows desktop app, inspect `%LOCALAPPDATA%\Ollama` as documented by
Ollama. See [Logging](logging.md).

## Collecting a useful report

Before filing a non-security issue, include:

- `ollamactl --version`;
- Windows/runtime architecture;
- Ollama version;
- the failing command with secrets removed;
- exit code and separate stderr;
- `health --output json` output;
- only the relevant redacted log excerpt;
- whether the endpoint is local or remote;
- exact reproduction steps.

Never attach a complete `.env` file, access token, private endpoint credential,
or unreviewed log bundle. Follow [SECURITY.md](../SECURITY.md) for suspected
vulnerabilities.

[Ollama troubleshooting guide]: https://docs.ollama.com/troubleshooting
[Ollama Windows guide]: https://docs.ollama.com/windows
