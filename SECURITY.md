# Security policy

## Supported versions

Version 0.3.0 is currently under development and has not yet been released.

| Version | Status |
|---|---|
| `main` / `0.3.0` development line | Receives security fixes |
| Earlier snapshots | Not supported |

This table will be updated when versioned releases exist.

## Reporting a vulnerability

Do not disclose a suspected vulnerability, exploit, secret, or sensitive log in
a public issue or pull request.

Use GitHub's private vulnerability reporting/security-advisory flow for this
repository when it is available:

<https://github.com/olatejulian/ollamactl/security/advisories/new>

If private reporting is unavailable, contact the repository owner through their
GitHub profile and request a private channel without including exploit details:

<https://github.com/olatejulian>

Include, through the private channel:

- affected version/commit and artifact runtime;
- impact and realistic threat scenario;
- minimal reproduction steps or proof of concept;
- whether secrets or third-party systems are involved;
- suggested remediation, if known;
- whether the issue has been disclosed elsewhere.

No response-time or remediation SLA is currently promised. The maintainer should
acknowledge, validate, coordinate a fix, prepare release notes, and agree on
disclosure timing before publication.

## Security-sensitive areas

Reports are especially valuable for:

- command/argument injection in native passthrough;
- path traversal, wildcard expansion, or unsafe artifact deletion;
- PID reuse or identity-validation flaws that could terminate another process;
- environment or secret disclosure through config, state, JSON, health, logs,
  errors, or build artifacts;
- unsafe dotenv parsing or execution;
- unauthorized remote API exposure;
- archive/package tampering or checksum mismatch;
- privilege-boundary or filesystem-permission mistakes;
- denial of service through unbounded HTTP, process, or log operations.

## Security design expectations

- Native arguments are separate process tokens; no shell evaluates user input.
- `server stop` requires recorded and live identity to match.
- Process discovery does not grant ownership.
- State is written atomically and does not contain the complete child
  environment.
- Dotenv input is parsed as data, never executed.
- Secret-safe config commands do not dump arbitrary values.
- Health log inclusion is explicit and bounded.
- Structured stdout stays separate from diagnostic stderr.
- Network and readiness operations use bounded timeouts and cancellation.
- Release artifacts include SHA-256 and machine-readable metadata.

These are intended properties, not a substitute for review or secure deployment.

## Deployment guidance

The local Ollama API does not require authentication by default. Do not bind or
forward it to an untrusted network without suitable network isolation and an
authenticated TLS proxy. Consult the official [Ollama API authentication]
documentation for cloud access behavior.

Protect the following as user-sensitive data:

- dotenv/config files;
- managed state and logs;
- model prompts and tool arguments;
- remote API credentials;
- release signing keys and CI secrets.

Run `ollamactl` with the least-privileged user that owns the intended Ollama
process and files. Do not elevate solely to work around an ownership mismatch.

## Logs and diagnostics

Before sharing a health report or log:

1. remove access tokens, credentials, private URLs, and personal paths;
2. remove prompt/model content not needed for reproduction;
3. keep only a bounded relevant excerpt;
4. rotate any credential that appeared in output;
5. use the private reporting path when security impact is possible.

## Release integrity

Verify `ollamactl.exe` against the included SHA-256 file. Code-signing status is
release-specific and must be stated in release notes; the project does not
currently promise Authenticode signing.

## License status

This repository currently has no declared software license. Security reporting
does not grant additional rights to use, copy, modify, or distribute the code.

[Ollama API authentication]: https://docs.ollama.com/api/authentication
