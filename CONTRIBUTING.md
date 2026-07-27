# Contributing to ollamactl

Thank you for helping improve `ollamactl`. Changes should preserve the project's
Clean Architecture boundaries, safe process-ownership model, stable automation
contract, and compatibility with the official Ollama CLI and REST API.

## Before you begin

- Search existing issues and pull requests before proposing duplicate work.
- Open an issue for a material command, architecture, or compatibility change so
  the contract can be agreed before implementation.
- Follow [SECURITY.md](SECURITY.md) for suspected vulnerabilities; do not open a
  public issue containing exploit or secret details.
- Read [Architecture](docs/architecture.md) and
  [Development](docs/development.md).

## License status

This repository does not currently declare a software license. Do not assume
that source availability grants permission to use, copy, modify, or distribute
the project. Prospective external contributors should obtain clarification from
the repository owner before submitting work that depends on licensing terms.

## Development setup

Required tools:

- .NET SDK selected by `global.json`;
- PowerShell 7.3 or newer;
- Pester 5 or newer;
- PSScriptAnalyzer;
- Git.

Ollama and a real model are optional for manual smoke tests; automated tests use
deterministic local fakes.

```powershell
dotnet restore .\ollamactl.slnx
dotnet build .\ollamactl.slnx -c Release --no-restore
.\scripts\Test.ps1 -Configuration Release -NoBuild -NoRestore
```

Run the full package gate before requesting release-related review:

```powershell
.\build.ps1
```

## Contribution workflow

1. Create a focused branch.
2. Keep unrelated user/worktree changes out of the patch.
3. Add or update tests before relying on manual verification.
4. Update generated help/README/docs when a public contract changes.
5. Add a concise entry to `CHANGELOG.md`.
6. Run format, build, tests, Pester, and PSScriptAnalyzer.
7. Review the diff for secrets, logs, state, model data, and generated artifacts.
8. Open a pull request describing behavior, risk, and evidence.

## Architecture rules

```text
Domain <- Application <- Infrastructure / CLI
```

- Domain owns stable business concepts and has no outward project dependency.
- Application owns use cases and ports and depends only on Domain.
- Infrastructure implements ports and owns HTTP/process/filesystem mechanisms.
- CLI owns parsing, presentation, exit-code mapping, and composition.
- PowerShell remains a thin executable wrapper.

Do not place process termination, HTTP calls, filesystem state, console output,
or JSON presentation in Domain/Application. Add or update architecture tests
when introducing a project relationship.

## C# changes

- Keep nullable/analyzer warnings at zero; warnings are errors.
- Preserve cancellation and bounded I/O.
- Pass native arguments with `ProcessStartInfo.ArgumentList`.
- Preserve stdout/stderr separation.
- Use source-generated JSON on publish-sensitive paths.
- Prefer immutable domain/application data.
- Add unit tests for policy, integration tests for adapters, and E2E tests for
  observable CLI contracts.

## PowerShell changes

- One public function per file.
- Use approved verbs, singular nouns, `CmdletBinding`, and complete help.
- Use terminating errors and declared output types.
- Use `SupportsShouldProcess` for state changes.
- Resolve user paths with `-LiteralPath`.
- Return objects; do not duplicate the C# HTTP/process implementation.
- Use `$TestDrive` and restore environment changes in Pester.
- Keep PSScriptAnalyzer clean under the repository settings.

## Documentation changes

- Document only implemented command contracts.
- Keep command names/options aligned with generated help.
- Link official Ollama material for upstream behavior.
- Do not include real tokens, private hosts, or unredacted logs.
- Do not add badges for services that are not configured.
- Do not add a license without an explicit owner decision.

## Pull-request checklist

- [ ] Change is focused and explained.
- [ ] Clean Architecture direction is preserved.
- [ ] Tests cover success and relevant failure/cancellation boundaries.
- [ ] `dotnet format --verify-no-changes` passes.
- [ ] `.\scripts\Test.ps1` passes.
- [ ] Public help and documentation are updated.
- [ ] `CHANGELOG.md` is updated.
- [ ] No secret, `.env`, log, state file, model, or artifact is committed.
- [ ] State-changing behavior has an explicit safety review.
- [ ] Native passthrough does not construct a shell command string.

## Reporting normal bugs

Include the smallest reproducible example, versions, OS architecture, command,
exit code, and separate stderr. Health JSON and bounded log excerpts are useful
after redaction. See [Troubleshooting](docs/troubleshooting.md).
