# Development

This guide covers a local source build, the test layers, and contribution rules
for version 0.3.0.

## Prerequisites

- Git;
- .NET SDK 10.0.302 or a compatible patch selected by `global.json`;
- PowerShell 7.3 or newer;
- Pester 5 or newer;
- PSScriptAnalyzer for the complete PowerShell quality gate;
- optional Ollama installation for manual smoke tests only.

Install PowerShell test tooling for the current user when needed:

```powershell
Install-Module Pester -Scope CurrentUser -MinimumVersion 5.0.0
Install-Module PSScriptAnalyzer -Scope CurrentUser
```

The deterministic automated suite uses a loopback fake API and does not need a
real model, GPU, or user daemon.

## Clone and verify the SDK

```powershell
git clone https://github.com/olatejulian/ollamactl.git
Set-Location .\ollamactl
dotnet --version
pwsh --version
```

The .NET version should satisfy `global.json`. Avoid overriding `DOTNET_ROOT` or
`MSBuildSDKsPath`; the build script temporarily aligns them with the selected
SDK and restores the process environment afterward.

## Repository layout

```text
src/
  Ollamactl.Domain/          domain models and rules
  Ollamactl.Application/     use cases, ports, configuration policy
  Ollamactl.Infrastructure/  HTTP, process, filesystem, JSON adapters
  Ollamactl.Cli/             command tree, rendering, composition root
  Ollamactl.PowerShell/      thin wrapper over ollamactl.exe
tests/
  Ollamactl.UnitTests/
  Ollamactl.ArchitectureTests/
  Ollamactl.IntegrationTests/
  Ollamactl.EndToEndTests/
  Ollamactl.TestKit/
  PowerShell/
docs/
scripts/
```

Read [Architecture](architecture.md) before changing project references.

## Restore, format, build

```powershell
dotnet restore .\ollamactl.slnx
dotnet format .\ollamactl.slnx --verify-no-changes --no-restore
dotnet build .\ollamactl.slnx -c Release --no-restore
```

The repository enables nullable reference types, recommended analyzers,
deterministic builds, code-style enforcement, and warnings as errors.

Run the development CLI without publishing:

```powershell
dotnet run --project .\src\Ollamactl.Cli -- --help
dotnet run --project .\src\Ollamactl.Cli -- --output json config show
```

The second `--` separates `dotnet run` options from application arguments.

## Test all layers

Use the repository driver:

```powershell
.\scripts\Test.ps1
```

Or run layers directly.

### .NET suites

```powershell
dotnet test .\ollamactl.slnx -c Release
```

| Suite | Purpose |
|---|---|
| Unit | Parsers, precedence, services, command handlers, rendering |
| Architecture | Enforce Clean Architecture dependency direction |
| Integration | Real loopback HTTP and process/state boundaries |
| End-to-end | Launch the CLI and assert stdout, stderr, JSON, and exit codes |
| TestKit | Shared deterministic fake Ollama API and process fixtures |

Set `OLLAMACTL_E2E_EXECUTABLE` to make E2E tests exercise a particular staged or
published file:

```powershell
$env:OLLAMACTL_E2E_EXECUTABLE = (Resolve-Path .\artifacts\win-x64\ollamactl.exe).Path
dotnet test .\tests\Ollamactl.EndToEndTests -c Release
```

Restore or remove the environment variable after the run.

### PowerShell suite

```powershell
$data = Import-PowerShellDataFile -LiteralPath .\PesterConfiguration.psd1
$configuration = New-PesterConfiguration -Hashtable $data
Invoke-Pester -Configuration $configuration
```

The suite verifies manifest/export consistency, complete help, literal-path
resolution, JSON conversion, `ShouldProcess`, native argument handling, and
terminating error behavior.

### PowerShell analysis

```powershell
Invoke-ScriptAnalyzer `
  -Path .\src\Ollamactl.PowerShell `
  -Recurse `
  -Settings .\PSScriptAnalyzerSettings.psd1
```

Do not suppress a rule without documenting why the rule is inappropriate for a
specific boundary.

## Full package build

```powershell
.\build.ps1
```

The build verifies formatting, compilation, tests, staged publication, and E2E
behavior of the exact executable before promoting artifacts. See
[Release process](release.md).

## Clean Architecture conventions

- Domain contains no references to Application, Infrastructure, or CLI.
- Application depends only on Domain and owns ports/use-case policy.
- Infrastructure implements Application ports and owns mechanisms.
- CLI owns presentation and composition, not business or process policy.
- PowerShell delegates to the executable and does not duplicate adapters.
- Domain/Application never call `Console`, `Environment.Exit`, HTTP clients, or
  `Process.Kill` directly.
- Infrastructure never chooses text tables or CLI exit codes.

Add an architecture test when introducing a new project or dependency rule.

## C# conventions

- Keep nullable warnings clean and treat analyzer warnings as errors.
- Prefer immutable records/value types for transported state.
- Accept and propagate `CancellationToken` for I/O and long operations.
- Use asynchronous I/O and `ConfigureAwait(false)` below the presentation layer.
- Use `ProcessStartInfo.ArgumentList` for native executables. Isolate unavoidable
  shell formats, such as Windows batch shims, behind dedicated validation and
  quoting with focused integration tests.
- Use source-generated JSON metadata for trimmed/AOT-sensitive paths.
- Use invariant culture for machine-readable values.
- Keep stdout/stderr writes behind CLI presentation abstractions.
- Name tests after observable behavior and include failure boundaries.

## PowerShell conventions

- One public function per file with an approved verb and singular noun.
- Complete comment-based help for public and private commands.
- `[CmdletBinding()]`, declared output types, terminating errors.
- `SupportsShouldProcess` for state-changing commands.
- `-LiteralPath` for user-supplied filesystem paths.
- Invoke native processes with separate argument tokens and preserve streams.
- Return objects from structured commands; do not format inside the module.
- Use `$TestDrive`, `InModuleScope`, and restored environment state in Pester.

## Adding or changing a CLI command

1. Confirm whether the route is REST, native passthrough, local orchestration,
   or combined health.
2. Add/update Domain types only for stable business concepts.
3. define Application ports and use-case behavior;
4. implement adapters in Infrastructure;
5. add a thin CLI handler and text/JSON renderer;
6. map errors to an existing stable exit category;
7. add unit, integration, E2E, and architecture coverage as applicable;
8. update the CLI reference and relevant operational guide;
9. update `CHANGELOG.md`.

Do not add a duplicate wrapper command when
`ollamactl ollama -- <args>` is the more accurate contract.

## Manual smoke test

Only after automated tests pass, on a machine where modifying local Ollama state
is intended:

```powershell
.\artifacts\win-x64\ollamactl.exe config show
.\artifacts\win-x64\ollamactl.exe health
.\artifacts\win-x64\ollamactl.exe model list
```

Starting/stopping a real server and loading models are state-changing and
resource-intensive; do them explicitly, not as part of the default test suite.

## Documentation changes

- Keep README concise enough to act as the landing page.
- Put full contracts in `docs/` and link them relatively.
- Do not document an unimplemented command or option.
- Use examples that do not contain real credentials or private addresses.
- Verify links and `git diff --check` before submitting.

See [CONTRIBUTING.md](../CONTRIBUTING.md) for the pull-request checklist.
