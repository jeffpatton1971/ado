# ado

A standalone Azure DevOps **Services** CLI for people and unattended automation.
Under development; Azure DevOps Server is not supported. See the
[approved design](docs/design.md) and [threat model](docs/threat-model.md).

## Current implementation

The initial foundation provides `--help`, clean semantic `--version`, safe JSON
errors, and `config paths`/`config show --effective`. See
[configuration and global parameters](docs/configuration.md). Service commands are not implemented yet. See the
[capability matrix](docs/capabilities.md); planned commands are not advertised as working.

## Development

Install the [.NET 10 LTS SDK](https://dotnet.microsoft.com/en-us/download/dotnet/10.0).
The repository pins 10.0.401 with patch roll-forward. Choose x64 for Intel/AMD systems
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

No Azure DevOps credentials are required for development tests. Do not place credentials
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
