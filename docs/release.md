# Release process

This document describes how versioned Windows artifacts are produced and
verified. Version 0.3.0 is currently unreleased.

## Release outputs

For each runtime and publish mode, a successful build creates:

```text
artifacts/<runtime>/
├── ollamactl.exe
├── ollamactl.exe.sha256
├── ollamactl.manifest.json
└── ollamactl-<version>-<runtime>-<mode>.zip
```

The ZIP contains the executable, checksum, and artifact manifest.

Supported build-script runtime identifiers:

- `win-x64`;
- `win-arm64`.

Publish modes:

- `singlefile`: default self-contained, trimmed, compressed single-file build;
- `aot`: optional self-contained Native AOT build.

`ollamactl.exe` does not bundle `ollama.exe`, GPU drivers, or models.

## Version sources

Before a release, align:

- `VersionPrefix` in `Directory.Build.props`;
- `ModuleVersion` in the PowerShell module manifest;
- the version heading in `CHANGELOG.md`;
- user-facing documentation and examples where the version is material.

Do not infer a release date. Add it only when the release is actually published.

## Build

From a clean checkout with the required SDK and PowerShell tooling:

```powershell
.\build.ps1 -Configuration Release -Runtime win-x64
```

For optional Native AOT:

```powershell
.\build.ps1 -Configuration Release -Runtime win-x64 -NativeAot
```

For ARM64:

```powershell
.\build.ps1 -Configuration Release -Runtime win-arm64
```

`-SkipTests` skips pre-publish test suites but does not bypass the staged
executable E2E gate. It is not appropriate for an official release build.

## Pipeline guarantees

The build script:

1. resolves the repository and safe staging paths;
2. snapshots relevant process environment variables;
3. aligns MSBuild hosts with the SDK selected by `global.json`;
4. restores packages;
5. verifies `dotnet format` has no changes;
6. builds with warnings as errors;
7. runs .NET, Pester, and PSScriptAnalyzer validation;
8. publishes into an isolated staging directory;
9. runs E2E tests against that exact staged executable;
10. reads the executable's reported version;
11. generates SHA-256 and artifact metadata;
12. creates the versioned ZIP;
13. promotes the package only after all mandatory gates pass;
14. removes staging data and restores the process environment.

The process is deterministic in compilation inputs, but release ZIP bytes and
manifest timestamps are not promised to be byte-for-byte reproducible across
machines.

## Verify artifacts

Inspect the manifest:

```powershell
$manifest = Get-Content -LiteralPath .\artifacts\win-x64\ollamactl.manifest.json -Raw |
  ConvertFrom-Json
$manifest
```

Verify SHA-256:

```powershell
$artifactRoot = '.\artifacts\win-x64'
$expected = ((Get-Content -LiteralPath "$artifactRoot\ollamactl.exe.sha256") -split '\s+')[0]
$actual = (Get-FileHash -LiteralPath "$artifactRoot\ollamactl.exe" -Algorithm SHA256).Hash.ToLowerInvariant()
if ($actual -ne $expected) { throw 'Checksum mismatch.' }
```

Smoke-test metadata and generated help:

```powershell
.\artifacts\win-x64\ollamactl.exe --version
.\artifacts\win-x64\ollamactl.exe --help
.\artifacts\win-x64\ollamactl.exe --output json config show
```

Do not start a real server or load a model in a release smoke test unless the
host is explicitly prepared for stateful testing.

## Release checklist

- [ ] Version sources agree.
- [ ] `CHANGELOG.md` accurately describes the release.
- [ ] Documentation matches generated help.
- [ ] Clean checkout build passes without `-SkipTests`.
- [ ] Architecture, unit, integration, E2E, and Pester tests pass.
- [ ] PSScriptAnalyzer produces no configured findings.
- [ ] Both checksum and manifest match the promoted executable.
- [ ] ZIP extracts and runs on a clean machine of the target architecture.
- [ ] `--output json` remains parseable with stderr separate.
- [ ] Native passthrough preserves an upstream exit code.
- [ ] Server stop cannot terminate an unrelated process.
- [ ] No `.env`, token, model data, log, state file, or test secret is packaged.
- [ ] Release notes disclose known limitations and code-signing status.
- [ ] The repository's lack of a declared software license is visible.

## GitHub release

Create a version tag and GitHub release only after the checklist passes. Attach
the versioned ZIP and, if desired for convenience, the standalone checksum and
manifest. Copy relevant entries from `CHANGELOG.md`; do not label an artifact
signed, reproducible, or supported on an untested platform without evidence.

This repository currently has no declared software license. Publishing a binary
does not by itself grant recipients permission to redistribute or modify it.

## Post-release

1. Verify download and checksum from the published release.
2. Run `--version` and `--help` on the downloaded file.
3. Mark the changelog version with the actual release date.
4. Open the next `Unreleased` section.
5. Document urgent corrections instead of silently replacing release assets.
