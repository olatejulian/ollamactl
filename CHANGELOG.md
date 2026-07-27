# Changelog

All notable changes to this project are documented in this file. The format is
based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and versions
are intended to follow [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [0.3.0] - Unreleased

Version 0.3.0 is the current development line and has not yet been published as
a release.

### Added

- Clean Architecture projects for Domain, Application, Infrastructure, and CLI.
- Architecture tests enforcing inward dependency direction.
- Hybrid routing across the Ollama REST API, native `ollama.exe`, and local
  lifecycle/process adapters.
- Native passthrough through `ollamactl ollama -- <args>` with separate native
  argument tokens, a dedicated Windows batch-shim path, and preserved
  streams/exit code.
- Structured status, model list/running/load/unload, chat, and tool-probe
  commands.
- Managed server start, stop, restart, status, and log commands.
- Ownership-aware process inventory with optional external process visibility.
- Complete health/doctor diagnosis with opt-in bounded log excerpts.
- Secret-safe configuration, env, and endpoint inspection.
- Self-contained Windows packaging for x64 and ARM64, with optional Native AOT.
- Staged executable E2E validation, SHA-256 output, artifact manifest, and ZIP.
- Unit, architecture, integration, end-to-end, TestKit, and Pester suites.
- PSScriptAnalyzer and Pester repository configuration.
- Complete README, architecture, CLI, configuration, operations, development,
  release, contributing, and security documentation.

### Changed

- Replaced the original mixed PowerShell operational implementation with a thin
  `Ollamactl.PowerShell` 0.3.0 wrapper over `ollamactl.exe`.
- Removed obsolete executable PowerShell scripts and the one-off model installer;
  their supported workflows now live in `ollamactl.exe` and the module cmdlets.
- Made JSON the automation contract while retaining human-readable text output.
- Centralized endpoint/timeout precedence and filesystem paths.
- Restricted server termination to a recorded and identity-validated owned
  process tree.
- Made build promotion conditional on successful tests of the staged binary.
- Isolated build/test SDK resolution from conflicting machine-level .NET
  variables and centralized publish intermediates by project.

### Security

- Native executables avoid shell command construction; unavoidable Windows
  batch shims use a dedicated validated quoting path.
- Dotenv content is parsed as data and is not echoed by config diagnostics.
- Managed process state is atomic and defends against PID reuse with identity
  checks.
- Log inclusion in health output is explicit and bounded.
- Release artifacts include SHA-256 and machine-readable metadata.

### Known project status

- No software license has yet been selected for the repository.
- Code-signing is not guaranteed; each future release must state its signing
  status.
- Version 0.3.0 remains unreleased until the release checklist is completed.
