# ado

A standalone Azure DevOps **Services** CLI for people and unattended automation.
Under development; Azure DevOps Server is not supported. See the
[approved design](docs/design.md) and [threat model](docs/threat-model.md).

## Current implementation

Available commands: `--help`, clean semantic `--version`, `config paths`,
`config show --effective`, `project list|get|search`, `auth check`, and local-only
`doctor`. PAT and externally supplied Entra tokens are supported through stdin,
environment, masked prompt, explicit argument, or native credential references.
See [configuration and global parameters](docs/configuration.md),
[credential setup](docs/authentication.md), [project examples](docs/projects.md), and
the [capability matrix](docs/capabilities.md).

Windows x64 tests pass locally, including a disposable synthetic Credential Manager
round-trip. macOS/Linux native keyring execution and live Azure DevOps validation are
pending. Pipelines, builds, classic releases, feed metadata, completion and download
commands remain planned; the initial release is not complete.

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
