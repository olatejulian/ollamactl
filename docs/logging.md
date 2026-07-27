# Logging

`ollamactl` captures stdout and stderr only for a server it starts and manages.
These logs are separate from logs created by the Ollama desktop application,
Windows services, containers, or other supervisors. For diagnosis, `server
logs` and `health --include-logs` also inspect supported official Ollama
`server.log` locations when those files are readable.

## Managed log paths

The default paths are:

```text
~/.config/ollama/logs/ollama.out.log
~/.config/ollama/logs/ollama.err.log
```

`--config-dir` relocates them:

```powershell
ollamactl --config-dir C:\Ollama\instance-a server status
ollamactl --config-dir C:\Ollama\instance-a server logs
```

Use `config show` or `server status` to discover the effective paths instead of
hard-coding them.

## Streams

- `ollama.out.log` receives stdout from the managed `ollama serve` target.
- `ollama.err.log` receives stderr from the managed target.
- CLI result output remains on the invoking terminal's stdout.
- CLI diagnostics remain on the invoking terminal's stderr.

Keeping streams separate makes failures observable without mixing JSON output
with diagnostic text.

## Reading logs

Use the wrapper command for normal inspection:

```powershell
ollamactl server logs
ollamactl --output json server logs
```

For a correlated, bounded report:

```powershell
ollamactl health --include-logs --log-tail 100
```

Read the files directly only when deeper manual inspection is necessary:

```powershell
Get-Content -LiteralPath ~/.config/ollama/logs/ollama.err.log -Tail 200
```

Use `-LiteralPath` when paths can contain wildcard characters.

## Upstream Ollama logs

On Windows and macOS, `ollamactl` includes the conventional official
`server.log` in its bounded log inspection when that file exists. Other logs,
and servers owned by services or containers, should be inspected through their
actual owner. The official [Ollama troubleshooting guide] documents current
locations:

- Windows desktop logs under `%LOCALAPPDATA%\Ollama`;
- macOS server logs under `~/.ollama/logs`;
- Linux service logs through `journalctl -u ollama`;
- container logs through the container runtime;
- a manually run `ollama serve` on that terminal's streams.

On Windows, the official [Ollama Windows guide] describes `app.log`,
`server.log`, `upgrade.log`, installation, model, and temporary locations.

## Debug logging

Enable upstream debug output in the server environment only for a bounded
investigation:

```dotenv
OLLAMA_DEBUG=1
```

Then restart the managed server and reproduce the issue:

```powershell
ollamactl server restart
ollamactl health --include-logs --log-tail 200
```

Disable verbose logging after diagnosis if it creates excessive or sensitive
output.

## Retention and growth

Managed logs are operational files, not an archival logging system. Do not
assume automatic rotation, retention, compression, or secure deletion. Monitor
their size on long-running hosts and apply an external retention policy while
the server is stopped or through an operations tool designed for log rotation.

Before deleting logs needed for a defect report, copy and redact only the
minimum relevant excerpt.

## Sensitive content

Logs can contain:

- hostnames, addresses, and local paths;
- model names and runtime configuration;
- request errors and fragments of upstream responses;
- hardware and driver information;
- prompt or tool data emitted by upstream components;
- credentials accidentally printed by external software.

Protect log directories with user-only permissions. Do not commit logs. Review
and redact content before sharing it in an issue, chat, or support channel. If a
secret appears, rotate it; redaction alone does not revoke exposure.

## Log troubleshooting

If `server logs` has no content:

1. run `server status` and confirm this config directory owns a server;
2. confirm `config show` points at the expected directory;
3. check whether the process is external rather than managed;
4. run `health` without logs for path and state evidence;
5. inspect filesystem permissions on the config/log directory;
6. use the upstream log location for desktop/service/container ownership.

[Ollama troubleshooting guide]: https://docs.ollama.com/troubleshooting
[Ollama Windows guide]: https://docs.ollama.com/windows
