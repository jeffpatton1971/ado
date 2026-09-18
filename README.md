# ado

`ado` is a standalone Azure DevOps **Services** CLI for humans and AI automation. It targets .NET 10 and runs on Windows, macOS and Linux. Its first milestone prioritizes authenticated, read-only pipeline diagnostics, selected build evidence, source context and Azure Artifacts packages. Azure DevOps Server/on-premises is not supported.

## Status

The release candidate is **1.0.0**, covering the M1 read-only diagnostics scope. Tagging and publication remain pending candidate CI and release review. See the [roadmap](docs/roadmap.md) for completion gates and the [capability matrix](docs/capabilities.md) for contracts and verification evidence.

| Area | Implemented commands |
|---|---|
| Setup and access | `config paths`, `config show`, `doctor`, `auth check` |
| Projects | `project list`, `project get`, `project search` |
| Pipelines | `pipeline list`, `pipeline get`, `pipeline runs`, `pipeline run get` |
| Build diagnostics | `build list`, `build get`, `build timeline`, `build diagnose`, `build logs`, `build log get` |
| Build outputs | `build artifact list`, `get`, `download`, `evidence` |
| Local archives | `artifact inspect`, `artifact extract`, `artifact evidence` |
| Feeds and packages | `feed list`, `feed get`, `package list`, `versions`, `version get`, `resolve`, `download`, `inspect` |
| Classic releases | `release list`, `get`, `environments`, `approvals`, `deployments`, `tasks`, `task log` |
| Explicit pipeline actions | `pipeline run start`, `pipeline run preview`, each with local dry-run and exact confirmation |

[Hosted CI](https://github.com/jeffpatton1971/ado/actions/runs/35360735378) passed tests and packaged-tool installation on Windows x64, Linux x64 and macOS Arm64, plus cross-compilation for six OS/architecture targets. Compilation is not runtime verification. The user also verified macOS installation and Keychain authentication, including non-interactive and repeated checks, plus downloads and selected-member evidence for both supported build artifact types. Linux native credential-store checks and live retry-history coverage remain open. See [verification](docs/testing.md).

## Workstation setup

Windows, macOS and Linux are supported platforms. Windows and macOS have also
been exercised through user-reported live commands; Linux support is backed by
hosted CI tests and packaged-tool checks. Linux Secret Service has not yet been
verified in a native desktop session. Report platform-specific problems through
[GitHub Issues](https://github.com/jeffpatton1971/ado/issues), including the OS,
architecture, ado version and redacted error output. Architecture-specific runtime
coverage is documented in [verification](docs/testing.md).

Building, testing and packing requires **Git and .NET SDK 10.0.400**. The runtime alone is insufficient. [global.json](global.json) pins that exact SDK with roll-forward disabled: installing only 10.0.401 or a later SDK does not satisfy the pin. Other SDK versions can coexist with 10.0.400.

| OS | Identify architecture | Result |
|---|---|---|
| Windows PowerShell | `Get-CimInstance Win32_ComputerSystem \| Select-Object SystemType` | `x64-based PC` = x64; `ARM64-based PC` = Arm64 |
| macOS | `uname -m` | `x86_64` = x64; `arm64` = Arm64 |
| Linux | `uname -m` | `x86_64` = x64; `aarch64`/`arm64` = Arm64 |

### Windows

Select the **10.0.400 SDK** installer for x64 or Arm64 from Microsoft's [.NET 10 downloads](https://dotnet.microsoft.com/en-us/download/dotnet/10.0). Use the older-release section if the page defaults to a newer SDK. [Windows installation guidance](https://learn.microsoft.com/en-us/dotnet/core/install/windows) covers prerequisites and installation options. A package manager's latest SDK may differ from the repository pin.

### macOS

Select the **10.0.400 SDK** installer from the same [download page](https://dotnet.microsoft.com/en-us/download/dotnet/10.0): Arm64 for Apple silicon or x64 for Intel. See Microsoft's [macOS installation guidance](https://learn.microsoft.com/en-us/dotnet/core/install/macos).

### Linux

Follow Microsoft's [distribution-specific instructions](https://learn.microsoft.com/en-us/dotnet/core/install/linux) for prerequisites. Install SDK **10.0.400** from an available versioned package or the matching SDK archive on the [.NET 10 download page](https://dotnet.microsoft.com/en-us/download/dotnet/10.0). An unversioned `dotnet-sdk-10.0` install may select a different SDK.

Open a new terminal and verify from inside the repository:

```text
dotnet --version
dotnet --info
dotnet --list-sdks
```

`dotnet --version` must resolve to `10.0.400`. Linux Secret Service libraries and a desktop keyring session are needed only for that credential provider; stdin and environment token input do not require a keyring.

## Build

The current implementation is on `feature/ado-foundation`:

```text
git clone --branch feature/ado-foundation https://github.com/jeffpatton1971/ado.git
cd ado
dotnet restore Ado.slnx --locked-mode
dotnet build Ado.slnx --configuration Release --no-restore
dotnet test Ado.slnx --configuration Release --no-build
```

Tests use synthetic data and mocked Azure DevOps HTTP responses. No Azure DevOps token is needed. The Windows native-store test creates and removes a uniquely named synthetic credential. See [testing](docs/testing.md) for platform coverage.

Run from source without installing:

```text
dotnet run --project src/Ado.Cli --configuration Release -- --help
```

In subsequent examples, replace `ado` with `dotnet run --project src/Ado.Cli --configuration Release --` when using source builds.

## Install `ado`

The package ID is **`PattonTech.Ado.Cli`**; the executable is **`ado`**. Until a release is published, build and pack locally. After the build above:

### Windows PowerShell

```powershell
dotnet pack src/Ado.Cli --configuration Release --no-build --output artifacts/packages
dotnet tool install --global PattonTech.Ado.Cli --version 1.0.0 --source (Resolve-Path artifacts/packages).Path
ado --version
ado --help
Get-Command ado
```

If the command is not found, add `%USERPROFILE%\.dotnet\tools` to your user `PATH` and open a new terminal. Check `Get-Command ado` before supplying credentials: other tools may use the same executable name.

### macOS (`zsh`) and Linux (`bash`)

```sh
dotnet pack src/Ado.Cli --configuration Release --no-build --output artifacts/packages
dotnet tool install --global PattonTech.Ado.Cli --version 1.0.0 --source "$PWD/artifacts/packages"
export PATH="$PATH:$HOME/.dotnet/tools"
ado --version
ado --help
command -v ado
```

Persist the PATH export in `~/.zprofile` on macOS, or the appropriate startup file such as `~/.profile` on Linux. Check which `ado` resolves before using it.

### Isolated installation and self-contained executables

To install into an explicit directory instead of globally:

```text
dotnet tool install PattonTech.Ado.Cli --version 1.0.0 --source artifacts/packages --tool-path .tools/ado
```

Run `.tools/ado/ado.exe` on Windows or `./.tools/ado/ado` on macOS/Linux. Both forms of tool installation require a compatible .NET 10 runtime. For repeatable development checks, `pwsh -File scripts/Test-ToolPackage.ps1` installs the newly packed version with an isolated cache without changing global PATH.

Self-contained output includes the runtime and targets one OS/architecture:

```text
dotnet publish src/Ado.Cli --configuration Release --runtime win-x64 --self-contained true --output artifacts/win-x64 -p:PackAsTool=false -p:NuGetLockFilePath=obj/publish-packages.lock.json
```

Substitute `win-arm64`, `linux-x64`, `linux-arm64`, `osx-x64` or `osx-arm64` in both the runtime and output path. These are the six CI build targets. The generated lock file leaves normal checked-in restore locks intact. Trimming and Native AOT are not enabled. Local packing/publishing creates files, not a public release.

## Configuration file location

One configuration document is selected in this order:

1. The `--config` option.
2. The `ADO_CONFIG` environment variable.
3. The OS default below.

| OS | Default path |
|---|---|
| Windows | `%APPDATA%\ado\config.json` |
| macOS | `~/Library/Application Support/ado/config.json` |
| Linux | `${XDG_CONFIG_HOME:-$HOME/.config}/ado/config.json` |

The CLI reads configuration; it does not create the file or parent directory. An absent default file uses built-in defaults; a missing explicitly selected file is an error. A repository-local `config.json` is ignored by Git but **not automatically discovered**. Select it explicitly:

```text
ado config paths --config ./config.json
ado config show --config ./config.json --effective
```

Alternatively, select a file for commands launched from the current shell:

```powershell
# Windows PowerShell
$env:ADO_CONFIG = "$env:APPDATA\ado\config.json"
```

```sh
# macOS
export ADO_CONFIG="$HOME/Library/Application Support/ado/config.json"
# Linux (use this line instead)
export ADO_CONFIG="${XDG_CONFIG_HOME:-$HOME/.config}/ado/config.json"
```

On macOS/Linux, after creating the directory and file, restrict their modes:

```sh
chmod 700 "$(dirname "$ADO_CONFIG")"
chmod 600 "$ADO_CONFIG"
```

Group/other-accessible configuration generates a warning. Windows uses file ACLs; restrict access through the file's Security properties. Configuration is strict JSON: comments, trailing commas, duplicate/unknown properties and plaintext token fields are rejected. See the [configuration reference](docs/configuration.md).

## Quick start

Save this as `config.json`, replacing the sample organization/project with yours:

```json
{
  "schemaVersion": 1,
  "defaultProfile": "work",
  "profiles": {
    "work": {
      "organization": "example-org",
      "project": "Example Project",
      "authentication": {
        "type": "pat",
        "provider": "environment"
      },
      "output": "table",
      "pagination": { "pageSize": 100, "limit": 100, "maxItems": 10000 }
    }
  }
}
```

The environment provider reads `ADO_TOKEN` when selected. For an interactive first check, explicitly override it with masked input; no environment token is needed:

```text
ado config show --config ./config.json --effective
ado doctor --config ./config.json --output table
ado auth check --config ./config.json --token-prompt --output table --read-only
ado project list --config ./config.json --token-prompt --output table --read-only --limit 20
```

`doctor` checks local configuration without retrieving credentials or making HTTP requests. `auth check` reads one selected endpoint. Project access does not prove build/feed access; use the probes below. Public access may succeed without independently proving credential validity.

Select another profile with `--profile`; profiles do not inherit settings or credentials from the default profile. Ordinary settings resolve from explicit flags, environment, selected profile, then defaults.

### Native credential stores

Choose the complete configuration for your operating system and save it as
`config.json` (or at the default location listed above). Replace `example-org` and
`Example Project` with your Azure DevOps organization and project. The `service`
and `account` values must match an existing credential-store item; the token stays
in that store and is never included in this file.

**macOS — Keychain (`macos-keychain`)**

```json
{
  "schemaVersion": 1,
  "defaultProfile": "work",
  "profiles": {
    "work": {
      "organization": "example-org",
      "project": "Example Project",
      "authentication": {
        "type": "pat",
        "provider": "macos-keychain",
        "service": "ado/example-org",
        "account": "azure-devops-pat"
      },
      "output": "table"
    }
  }
}
```

**Windows — Credential Manager (`windows-credential-manager`)**

```json
{
  "schemaVersion": 1,
  "defaultProfile": "work",
  "profiles": {
    "work": {
      "organization": "example-org",
      "project": "Example Project",
      "authentication": {
        "type": "pat",
        "provider": "windows-credential-manager",
        "service": "ado/example-org",
        "account": "azure-devops-pat"
      },
      "output": "table"
    }
  }
}
```

**Linux — Secret Service (`linux-secret-service`)**

```json
{
  "schemaVersion": 1,
  "defaultProfile": "work",
  "profiles": {
    "work": {
      "organization": "example-org",
      "project": "Example Project",
      "authentication": {
        "type": "pat",
        "provider": "linux-secret-service",
        "service": "ado/example-org",
        "account": "azure-devops-pat"
      },
      "output": "table"
    }
  }
}
```

| OS | Provider | Item fields matching the reference |
|---|---|---|
| Windows | `windows-credential-manager` | Generic Credential target = `service`; user name = `account` |
| macOS | `macos-keychain` | Generic Password name/service = `service`; account = `account` |
| Linux | `linux-secret-service` | String attributes `service` and `account` |

The item's password/secret is the token. These lookup labels are not tokens, and `account` need not be an email address. `ado` reads existing credentials; it does not store them. See [native setup and limitations](docs/authentication.md) for provisioning instructions and Linux dependencies.

Validate any of these files locally, then check access using its configured store:

```text
ado doctor --config ./config.json --output table
ado auth check --config ./config.json --output table --read-only
```

Omit `--token-prompt` and `--token-stdin` when testing the configured store: those
flags select a different credential source. Linux requires libsecret and an
available Secret Service session/keyring, as described in the setup reference.

For first-use macOS Keychain authorization, run this without `--non-interactive` or `--token-prompt` after configuring the `macos-keychain` provider:

```sh
ado auth check --config ./config.json --output table --read-only
ado auth check --config ./config.json --non-interactive --json --read-only
```

Approve the expected executable on the first invocation; choose **Always Allow** if subsequent unattended access is intended. The second invocation verifies prompt-free access. Moving or rebuilding the executable may require approval again. Successful macOS Keychain access, including non-interactive and repeated checks, is user-verified. Linux native-store and macOS failure/re-authorization paths remain unverified.

### Automation

For a PowerShell 7 one-off check, let the shell collect masked input and pass it through stdin:

```powershell
Read-Host "Token" -MaskInput | ado auth check --config ./config.json --token-stdin --non-interactive --json --read-only
```

For CI with `ADO_TOKEN` already injected by its secret manager:

```text
ado auth check --config ./config.json --non-interactive --json --read-only
```

With a token already supplied in a shell variable on macOS/Linux:

```sh
printf '%s\n' "$ADO_TOKEN" | ado auth check --config ./config.json --token-stdin --non-interactive --json --read-only
```

JSON mode is non-interactive and cannot be combined with `--token-prompt`. `--token` is supported but warns because process listings/history can expose it. For an externally acquired Azure DevOps Entra access token, add `--auth-type entra-token` or set the profile's authentication type accordingly. Automatic Entra login, acquisition and refresh are not implemented.

### Microsoft Entra ID access tokens

`ado` supports existing Azure DevOps Entra access tokens with authentication type
`entra-token`, sending them as Bearer tokens. Token acquisition is external: `ado`
does not sign in, invoke Azure CLI, refresh tokens or persist a login. An expired
token must be replaced by your external authentication process.

For CI or another process that injects the access token into `ADO_TOKEN`, use this
complete configuration:

```json
{
  "schemaVersion": 1,
  "defaultProfile": "work",
  "profiles": {
    "work": {
      "organization": "example-org",
      "project": "Example Project",
      "authentication": {
        "type": "entra-token",
        "provider": "environment"
      },
      "output": "table"
    }
  }
}
```

With `ADO_TOKEN` supplied by your secret manager:

```text
ado auth check --config ./config.json --non-interactive --json --read-only
```

For a local user, sign in separately with `az login` using the tenant connected to
your Azure DevOps organization. Follow [Microsoft's Azure CLI token guidance](https://learn.microsoft.com/en-us/azure/devops/cli/entra-tokens?view=azure-devops)
to select the appropriate account/subscription. Then pipe an Azure DevOps token
directly into `ado` (PowerShell 7, bash or zsh):

```text
az account get-access-token --resource 499b84ac-1321-427f-aa17-267ca6975798 --query accessToken --output tsv | ado auth check --config ./config.json --auth-type entra-token --token-stdin --non-interactive --json --read-only
```

The explicit `--auth-type` also makes this command work with a PAT-configured
profile. The resource ID requests an Azure DevOps token; an Azure Resource Manager
token is not interchangeable. The signed-in identity still needs Azure DevOps
access. Azure CLI manages its own sign-in/cache independently of `ado`.

If you already have an access token, PowerShell can collect it through masked input:

```powershell
Read-Host "Entra access token" -MaskInput | ado auth check --config ./config.json --auth-type entra-token --token-stdin --non-interactive --json --read-only
```

Native stores also work with Entra tokens. In any of the macOS, Windows or Linux
configurations above, set `authentication.type` to `entra-token`, retain that OS's
provider, and use a separate existing credential reference such as service
`ado/example-org/entra` and account `azure-devops-access-token`. Its secret must
contain the access token, not a client secret or refresh token. `ado` only reads
the item; external tooling must replace it when the token expires.

Bearer transport and stdin selection have synthetic tests. Live Entra sign-in and
service access have not yet been user-verified. Service-principal, managed-identity
and federated token acquisition are not built-in providers.

## Examples

These examples use illustrative IDs. Replace IDs, names, versions and the run URL with values from your organization; do not paste angle-bracket placeholders into PowerShell. Remote examples use masked prompts. For automation, substitute a native provider, stdin or injected environment token, then add `--non-interactive --json`.

### Find a run and investigate a failure

```text
ado pipeline list --config ./config.json --token-prompt --read-only --limit 20
ado pipeline runs --pipeline-id 12 --config ./config.json --token-prompt --read-only --limit 10
ado build list --definition-id 12 --branch refs/heads/main --config ./config.json --token-prompt --read-only --limit 20
ado build diagnose --run-url "https://dev.azure.com/example-org/Example%20Project/_build/results?buildId=34" --include-history --config ./config.json --token-prompt --read-only --limit 100
ado build timeline --build-id 34 --include-history --config ./config.json --token-prompt --read-only --limit 100
ado build log get --build-id 34 --log-id 21 --config ./config.json --token-prompt --read-only --limit 100
```

Select the log ID from diagnosis/timeline; `21` is illustrative. Diagnosis reports failures, skips and referenced attempts without inferring their cause or automatically reading logs. A failed build can be successfully inspected with exit 0.

For repository-scoped PR validation builds and run resource versions:

```text
ado build list --definition-id 12 --repository-id owner/repo --repository-type GitHub --pr-number 8 --config ./config.json --token-prompt --read-only --limit 100
ado pipeline run get --pipeline-id 12 --run-id 34 --config ./config.json --token-prompt --read-only
```

`build list --source-sha` accepts a full 40-character Git SHA. PR searches cover supported merge refs; the built merge commit is not asserted to be the PR head. Run resource versions are service-reported, not independently verified template provenance.

### Download and inspect build evidence

Run from a writable directory. Destinations must be new files with existing, non-symlink parents; choose new names when repeating these commands.

```text
ado build artifact list --build-id 34 --config ./config.json --token-prompt --read-only
ado build artifact download --build-id 34 --artifact-name CompiledOutputs --destination ./outputs-34.zip --config ./config.json --token-prompt --read-only
ado artifact inspect --file ./outputs-34.zip --read-only --limit 100
ado artifact inspect --file ./outputs-34.zip --entry CompiledOutputs/src/plugin.json --show-text --read-only
ado artifact extract --file ./outputs-34.zip --entry CompiledOutputs/src/plugin.json --destination ./plugin-34.json --read-only
ado build artifact evidence --build-id 34 --artifact-name CompiledOutputs --entry CompiledOutputs/src/plugin.json --destination ./evidence-34.json --config ./config.json --token-prompt --read-only
```

Choose exact member paths from the inventory. Member selection occurs after the artifact download; it does not reduce network transfer. Local operations need no config/token. Evidence exports omit raw member contents and signed URLs; names may still be sensitive. SHA-256 identifies bytes, not publisher authenticity.

### Feeds and exact NuGet packages

Use `--scope organization` for an organization-level feed even when a project is configured. Project scope is the default.

```text
ado feed list --scope organization --config ./config.json --token-prompt --read-only
ado feed get --scope organization --feed automation --config ./config.json --token-prompt --read-only
ado package list --scope organization --feed automation --protocol NuGet --config ./config.json --token-prompt --read-only --limit 20
ado package resolve --scope organization --feed automation --name Json.Input.Provider --package-version 1.1.0 --config ./config.json --token-prompt --read-only
ado package download --scope organization --feed automation --name Json.Input.Provider --package-version 1.1.0 --destination ./Json.Input.Provider.1.1.0.nupkg --config ./config.json --token-prompt --read-only
ado package inspect --file ./Json.Input.Provider.1.1.0.nupkg --read-only
```

Resolution requires an exact name and literal reported version; it does not select latest, solve version ranges or restore dependencies. `package inspect` reports nuspec identity and dependency declarations without loading assemblies or establishing compatibility. Feed/package visibility does not prove download permission.

### Check service access and classic releases

```text
ado auth check --capability build --build-id 34 --config ./config.json --token-prompt --read-only
ado auth check --capability artifact --build-id 34 --config ./config.json --token-prompt --read-only
ado auth check --capability feed --scope organization --feed automation --config ./config.json --token-prompt --read-only
ado release list --config ./config.json --token-prompt --read-only --limit 20
ado release environments --release-id 42 --config ./config.json --token-prompt --read-only
ado release tasks --release-id 42 --config ./config.json --token-prompt --read-only
```

Classic release approvals/deployments are inspected, not approved or deployed by these commands. YAML environment checks are a separate planned capability.

### Preview an intended pipeline action locally

```text
ado pipeline run start --pipeline-id 12 --config ./config.json --dry-run --read-only --json
ado pipeline run preview --pipeline-id 12 --config ./config.json --dry-run --read-only --json
```

These local plans need no token and send no request. Actual run submission and server YAML preview require their own exact `--confirm` value and are blocked by `--read-only`. See [pipeline action contracts](docs/pipelines.md) before executing either.

## Safety

- `--read-only` blocks remote writes, including server YAML preview. Explicit local downloads, extraction and evidence files remain allowed.
- `--dry-run` prevents action submission; it does not suppress ordinary read requests. Local plans do not validate service permissions or pipeline YAML.
- Run start/server preview require exact target confirmation. Writes are not automatically retried after ambiguous delivery; uncertain outcomes use exit 8.
- HTTPS certificate validation stays enabled. Service URLs are constructed from validated context. Download redirects are restricted to permitted storage hosts and receive no service credentials or cookies.
- Reads, archives and downloads have time/size/count bounds. Extraction never overwrites or executes content; unsafe paths, links and collisions are rejected. Use canonical paths when a macOS temporary-directory alias is a symlink.
- Configuration rejects plaintext tokens. Selected credentials are redacted from service text, but arbitrary log/artifact secrets are not automatically sanitized. Raw logs, YAML and member text appear only when requested.
- JSON/non-interactive commands never prompt. Resource text is treated as data.

See the [threat model](docs/threat-model.md) and [authentication guide](docs/authentication.md).

## Output and exit codes

Human output defaults to tables; some single-object commands print indented JSON in table mode. `--json` (or `--output json`) emits a machine-readable envelope. Success includes `ok`, `data` and `meta.schemaVersion` (currently 1). Errors include `ok:false` and `error` with a stable code/message; plain errors do not include `meta`. `--version` always prints the clean semantic version, even with `--json`.

Check `meta.completeness`, `truncated`, `continuationToken` and `scannedCount` where provided. `--require-complete` on supported bounded reads returns exit 10 for partial or unknown completeness, retaining safely parsed data where available. `--all` uses configured ceilings, not an unbounded scan. Continuation semantics vary by endpoint; resume with the same context and filters.

| Code | Meaning |
|---:|---|
| 0 | Successful operation, including inspection of a failed build |
| 1 | Unexpected internal error |
| 2 | Command syntax or argument error |
| 3 | Configuration error |
| 4 | Credential/authentication failure |
| 5 | Authorization failure |
| 6 | Resource not found; visibility may mask existence |
| 7 | Safety/read-only refusal, unsafe path or destination conflict |
| 8 | Write outcome uncertain |
| 9 | Service/transport failure, invalid response or timeout |
| 10 | Incomplete result or exceeded result/content bound |
| 130 | Cancelled, unless a dispatched write has an uncertain outcome |

## Documentation

- [Configuration and global options](docs/configuration.md), [credential setup](docs/authentication.md)
- [Projects](docs/projects.md), [pipelines](docs/pipelines.md), [builds and logs](docs/builds.md)
- [Archive inspection and extraction](docs/artifact-inspection.md), [feeds](docs/feeds.md), [packages](docs/packages.md)
- [Classic releases](docs/releases.md), [capability matrix](docs/capabilities.md)
- [Design](docs/design.md), [dependencies](docs/dependencies.md), [threat model](docs/threat-model.md)
- [Verification and CI](docs/testing.md), [requirements and milestones](docs/roadmap.md)

## Changes and releases

Work proceeds in small commits under [repository instructions](AGENTS.md). [Directory.Build.props](Directory.Build.props) is the authoritative CLI/package version; [CHANGELOG.md](CHANGELOG.md) records changes. Every branch merged into `main` with code or documentation changes must include an intentional Semantic Versioning update and matching changelog update before merge. Do not reuse a version for different source states on `main`; an unmerged development branch may span multiple commits before its pre-merge version update.

M1 completion is the intended first tagged-version checkpoint. Before a release, verify version, changelog, package identity, documentation, platform acceptance and that the version has not already been published. Tags, GitHub releases, NuGet publication, release-executable uploads, pushes, PR creation/merge and release-workflow dispatch are **human-only**. Agents prepare local commits and report a candidate source state for a human to push, merge or release. Verification CI may run automatically after a human push. The existing branch and CI history does not constitute a release. See [repository instructions](AGENTS.md) and [Copilot instructions](.github/copilot-instructions.md).

## Official references

- [.NET 10 SDK downloads](https://dotnet.microsoft.com/en-us/download/dotnet/10.0)
- [Azure DevOps REST API reference](https://learn.microsoft.com/en-us/rest/api/azure/devops/)
- [Azure DevOps authentication guidance](https://learn.microsoft.com/en-us/azure/devops/integrate/get-started/authentication/authentication-guidance?view=azure-devops)
- [Personal access tokens](https://learn.microsoft.com/en-us/azure/devops/organizations/accounts/use-personal-access-tokens-to-authenticate?view=azure-devops)

Licensed under [GNU AGPL version 3](LICENSE). No warranty.
