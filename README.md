# ado

A standalone Azure DevOps **Services** CLI for people and unattended automation.
Under development; Azure DevOps Server is not supported. See the
[approved design](docs/design.md), [prioritized requirements and milestones](docs/roadmap.md),
and [threat model](docs/threat-model.md). The MVP prioritizes authenticated, read-only
run diagnostics, selected build evidence, exact source provenance and package inspection.

## Current implementation

Available commands: `--help`, clean semantic `--version`, `config paths`,
`config show --effective`, `project list|get|search`, `auth check`, local-only
`doctor`, `pipeline list|get|runs`, `pipeline run get|start|preview`, `build list|get|logs`,
`build timeline`, `build diagnose`, `build log get`, `build artifact list|get|download`, and
`release list|get|environments|approvals|deployments|tasks`, and `release task log`. Run start and
server preview include a local dry-run and require exact target confirmation before
sending their requests. PAT and externally supplied Entra tokens are supported through stdin,
environment, masked prompt, explicit argument, or native credential references.
See [configuration and global parameters](docs/configuration.md),
[credential setup](docs/authentication.md), [project examples](docs/projects.md),
[pipeline examples and limits](docs/pipelines.md), [build inspection](docs/builds.md),
[classic releases](docs/releases.md), and
the [capability matrix](docs/capabilities.md).

`artifact inspect` lists a downloaded ZIP's entries and computes archive/selected
member hashes locally. Add --show-text to read bounded UTF-8 member text.
See [artifact inspection](docs/artifact-inspection.md) for
fixed bounds and exact selection. `artifact extract` writes one exact member to
an explicit new file with no overwrite; bulk directory extraction is not supported.
`artifact evidence` exports selected member metadata, hashes and verification limits
to a new JSON file; optional run/artifact labels remain explicitly unverified.
`build artifact evidence` reads authenticated metadata, downloads the artifact and
exports selected member hashes with the observed build/artifact association.

Windows x64 tests pass locally, including a disposable synthetic Credential Manager
round-trip. User-run live project listing/retrieval and pipeline listing/run-history checks
succeeded. macOS/Linux native keyring execution and live run submission remain pending.
User-run server YAML preview, build list/get and log index/content reads also succeeded.
Build-output metadata list/get and a PipelineArtifact ZIP download passed user-run
live checks; Container download live verification remains pending. Classic-release
inspection through task logs also passed user-run checks. Build inspection accepts
--run-url (mock-tested; user-verified timeline read for build 18722). Richer
diagnostics, exact provenance, archive inspection and feed/package access remain
MVP gaps. The initial release is not complete; see the backlog for acceptance criteria.

The [CI definition](.github/workflows/ci.yml) covers three-OS tests and six-RID
cross-compilation but has not yet run remotely. See [verification status](docs/testing.md).

## Development

Install the [.NET 10 LTS SDK](https://dotnet.microsoft.com/en-us/download/dotnet/10.0).
The repository pins SDK 10.0.400 exactly, without automatic roll-forward. CI reads
the same version from `global.json`. Choose x64 for Intel/AMD systems
or Arm64 for ARM systems. Windows: Settings > System > About > System type;
macOS: About This Mac (Apple silicon is Arm64); Linux: `uname -m`
(`x86_64` is x64, `aarch64` is Arm64).

Use Microsoft's native installer on Windows/macOS, or the documented distribution
package instructions on Linux. After opening a new terminal:

```text
dotnet restore Ado.slnx --locked-mode
dotnet build Ado.slnx --no-restore
dotnet test Ado.slnx --no-build
dotnet run --project src/Ado.Cli -- --version
```

No Azure DevOps credentials are required for development tests. The Windows native
test creates and deletes a uniquely named synthetic local credential. Do not place credentials
in test files or commit configuration containing secrets. Local SDKs may be placed in
the ignored `.tools` directory. Packaging and installation instructions will be completed
with the corresponding capabilities; no package or binary has been published.

## Changes and releases

Work proceeds in small, coherent commits on a feature branch. `Directory.Build.props`
is the authoritative product version; [CHANGELOG.md](CHANGELOG.md) records user-visible
changes. Every merge must explicitly decide whether a version change is required.
An unpublished development version may span multiple commits. Before a release/merge,
verify version, changelog, package identity, clean `--version`, documentation, supported
platform tests, and that the version has not already been published. Publishing is a
separate explicitly authorized action.

The executable name `ado` overlaps other tools. Check which executable your PATH resolves
before using credentials. The package identity is `PattonTech.Ado.Cli`.

Licensed under [GNU AGPL version 3](LICENSE). No warranty.
