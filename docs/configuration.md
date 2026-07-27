# Configuration

`ollamactl` separates control-plane configuration (how the wrapper connects and
where it stores state) from the environment passed to `ollama serve`. This
prevents accidental coupling between a remote API request and local process
management.

## Default layout

Unless overridden, the user-scoped configuration root is:

```text
~/.config/ollama/
├── .env
├── logs/
│   ├── ollama.out.log
│   └── ollama.err.log
└── run/
    └── ollamactl-server.json
```

On Windows, `~` resolves to the current user's profile directory. Use
`ollamactl config show` to see normalized paths for the current invocation.

## Precedence

Effective wrapper values use the following order, highest priority first:

1. command-line option;
2. process environment;
3. selected dotenv file;
4. built-in default.

This order lets CI override a developer file without editing it and lets an
explicit command remain unambiguous.

## Wrapper settings

| Setting | Command line | Process environment | Default |
|---|---|---|---|
| API host | `--host` | `OLLAMA_HOST` | `127.0.0.1:11434` |
| Timeout | `--timeout` | `OLLAMACTL_TIMEOUT_SECONDS`, then compatibility `OLLAMA_STARTUP_TIMEOUT` | `30` seconds |
| Config root | `--config-dir` | `OLLAMACTL_CONFIG_DIR` | `~/.config/ollama` |
| Env file | `--env-file` | — | `<config-root>/.env` |
| Ollama executable | `--ollama-path` | `OLLAMA_EXE` | application on `PATH` |
| Output | `--output` | — | `text` |

Timeout values must be between 1 and 600 seconds. Hosts can be written as
`host:port` or an HTTP(S) origin without a path, query, or fragment;
`ollamactl endpoint` shows the normalized result.

`OLLAMACTL_EXE` is used by the optional PowerShell module to locate
`ollamactl.exe`; it is distinct from `OLLAMA_EXE`, which locates the upstream
Ollama executable.

## Dotenv file

Copy the repository template and edit only the values needed by the server:

```powershell
New-Item -ItemType Directory -Path ~/.config/ollama -Force | Out-Null
Copy-Item .\.env.example ~/.config/ollama/.env
```

Example:

```dotenv
OLLAMA_HOST=127.0.0.1:11434
OLLAMA_MODELS=D:\Ollama\Models
OLLAMA_KEEP_ALIVE=30m
OLLAMA_CONTEXT_LENGTH=8192
OLLAMA_DEBUG=1
```

The parser treats the file as key/value data, not as a PowerShell, batch, or
shell program. Blank lines and comments are supported. Variable expansion and
arbitrary commands are not evaluated. Malformed entries cause a configuration
error rather than being silently ignored.

When starting a managed server, values already present in the current process
environment take precedence over the dotenv file. This makes explicit CI or
terminal settings authoritative.

For the upstream list and semantics of server environment variables, run:

```powershell
ollamactl ollama -- serve --help
```

The official [Ollama CLI reference] also documents `ollama serve` and directs
users to its generated environment help.

## Frequently used Ollama variables

The following are examples, not a replacement for upstream documentation:

| Variable | Typical purpose |
|---|---|
| `OLLAMA_HOST` | Bind/connect address |
| `OLLAMA_MODELS` | Model storage directory |
| `OLLAMA_KEEP_ALIVE` | Default model residency |
| `OLLAMA_CONTEXT_LENGTH` | Default context length |
| `OLLAMA_LOAD_TIMEOUT` | Upstream model-load timeout |
| `OLLAMA_MAX_LOADED_MODELS` | Loaded model limit |
| `OLLAMA_MAX_QUEUE` | Request queue limit |
| `OLLAMA_NUM_PARALLEL` | Parallel request limit |
| `OLLAMA_FLASH_ATTENTION` | Upstream attention implementation switch |
| `OLLAMA_KV_CACHE_TYPE` | KV-cache type |
| `OLLAMA_DEBUG` | Additional upstream diagnostic logging |
| `OLLAMA_NO_CLOUD` | Upstream cloud-feature policy |
| `OLLAMA_ORIGINS` | Allowed browser origins |
| `HTTPS_PROXY`, `NO_PROXY` | Network proxy behavior |

Values are passed to Ollama; validation beyond basic dotenv syntax belongs to
the upstream executable.

## Inspecting effective configuration

```powershell
ollamactl config show
ollamactl config env
ollamactl endpoint
```

Use `--output json` for automation. `config show` reports sources such as
`command-line`, `environment`, `env-file`, and `default`. Secret-safe
configuration commands do not print the complete dotenv file or expose token
values.

## Multiple managed instances

Each configuration directory has its own state and managed logs:

```powershell
ollamactl --config-dir C:\Ollama\instance-a server start
ollamactl --config-dir C:\Ollama\instance-b server start
```

Each instance must still use a non-conflicting `OLLAMA_HOST`. A different state
directory does not make two servers able to bind the same address.

Always use the same `--config-dir` when checking, restarting, logging, or
stopping a specific managed instance.

## Remote endpoints

REST-backed commands can target a remote Ollama API:

```powershell
ollamactl --host http://ollama.internal:11434 status
```

The official API is available locally without authentication by default. Do not
expose an unauthenticated Ollama port to an untrusted network. Use network
segmentation and an authenticated TLS reverse proxy where remote access is
required. Direct cloud API usage has separate authentication requirements in
the official [Ollama API authentication] documentation.

Local `server` and `process` commands always describe the local machine; a
remote `--host` does not grant control over a remote operating-system process.

## Secret handling

- Keep `.env` out of version control; the repository ignores it.
- Commit only `.env.example` with non-secret placeholders.
- Restrict read access to the configuration directory.
- Do not place credentials directly on a shared command line when an environment
  mechanism is available.
- Treat diagnostic bundles and logs as sensitive until reviewed.
- Prefer `config show` / `config env` over printing the dotenv file.
- Rotate a credential if it appears in terminal history, a log, or a report.

## PowerShell module executable resolution

The module resolves the wrapper in this order:

1. `-ExecutablePath` on the PowerShell command;
2. `OLLAMACTL_EXE`;
3. an application named `ollamactl.exe` or `ollamactl` on `PATH`.

Explicit paths are resolved literally, and native arguments are passed as
separate tokens without shell evaluation.

[Ollama CLI reference]: https://docs.ollama.com/cli
[Ollama API authentication]: https://docs.ollama.com/api/authentication
