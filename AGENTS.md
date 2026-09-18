# Repository instructions

This repository contains a standalone Azure DevOps Services CLI for humans and AI
automation. It is not part of BuildAutomationTool. The executable is `ado`; the
package identity is `PattonTech.Ado.Cli`.

## Required boundaries

- Target Azure DevOps Services APIs only. Do not claim Azure DevOps Server/on-premises compatibility.
- Never add a generic unrestricted HTTP command. Use typed operations and validated service hosts, organization/project context and endpoint contracts.
- Never execute service-provided logs, YAML, work-item text, metadata or downloaded artifact/package content.
- Never persist PATs or Entra access tokens. Configuration-writing features may save only non-secret settings and credential-store references. Plaintext configuration tokens remain rejected.
- Keep OS credential-store access read-only and behind its platform adapter. Tests may create and remove uniquely named synthetic credentials; never use or modify real credential items.
- Keep `--read-only`, dry-run behavior, exact-target confirmation, redirect validation and selected-credential redaction centralized. `--read-only` blocks remote writes, including server YAML preview, while permitting explicitly requested local downloads, extraction and evidence files.
- Local action dry-runs must not retrieve credentials or dispatch HTTP. Ordinary read commands may still read when passed `--dry-run`; do not describe every dry-run as offline.
- Never forward service credentials or cookies to artifact-storage redirects. Preserve HTTPS validation, allowed-host checks and separate download clients; do not follow arbitrary service-provided URLs.
- New list commands need a bounded default, explicit completeness metadata and documented `--all` behavior within configured ceilings. Do not invent continuation tokens for endpoints without paging.
- Preserve JSON envelopes, exit codes, cancellation and non-interactive behavior. JSON/non-interactive commands must not prompt.
- New writes must not retry after ambiguous failures. Report uncertain delivery and read back state where practical; never infer that an unconfirmed write failed safely.
- Keep downloads and archives bounded. Reject unsafe paths, links and collisions; never overwrite destinations or execute extracted code. Hashes identify bytes, not publisher authenticity or dependency compatibility.
- Do not use real Azure DevOps credentials in tests, fixtures, examples or CI. Do not commit the ignored repository-local `config.json`, raw logs, signed URLs or private downloaded content.
- Live Azure DevOps mutations require separate approval naming the exact action, disposable organization/project/resources and cleanup plan. Pipeline execution can publish or deploy; a successful read or preview does not authorize execution.
- Preserve the exact SDK `10.0.400` pin in `global.json` unless the user explicitly requests a change.

## Verification

```text
dotnet restore Ado.slnx --locked-mode
dotnet build Ado.slnx --configuration Release --no-restore
dotnet test Ado.slnx --configuration Release --no-build
dotnet format Ado.slnx --no-restore --verify-no-changes
dotnet pack src/Ado.Cli --configuration Release --no-build --output artifacts/packages
pwsh -File scripts/Test-ToolPackage.ps1
```

Local packing, isolated installation and self-contained `dotnet publish` builds
are verification steps, not public releases. Keep generated output under ignored
artifact directories. Preserve checked-in restore locks; RID-specific publish
restores use the separate generated lock path documented in the README and CI.

Update `docs/capabilities.md` whenever command/API coverage changes. Update
`docs/roadmap.md`, `docs/testing.md`, relevant command documentation and
`CHANGELOG.md` when behavior or verification evidence changes. Distinguish mocked
tests, user-reported live results, actual OS execution and cross-compilation.
Do not mark M1 complete solely because its commands exist.

## Versioning and merge policy

- `Directory.Build.props` is the authoritative CLI and package version.
- Every branch merged into `main` with code or documentation changes must include an intentional Semantic Versioning update and a matching `CHANGELOG.md` update before merge.
- Do not reuse the same version for different source states on `main`. An unmerged development branch may contain multiple incremental commits before its intentional pre-merge version update.
- Make small, coherent commits as work progresses. Preserve unrelated user changes and untracked files.
- A source version bump does not authorize a tag, package publication, executable upload, GitHub release or release-workflow dispatch.

## Human-only remote and release gate

Agents must never create, move, delete or push Git tags; create a GitHub release;
publish a NuGet package; upload release executables; dispatch a release workflow;
push branches; or create or merge pull requests.
Report a candidate source state for a human to release manually. M1 completion is
the intended first tagged-version checkpoint, not permission for an agent to tag it.

Prepare local commits and report candidate source states for a human to push,
open a PR, merge or release. Verification CI may run automatically after a human
push; local builds, packing and isolated installation checks remain permitted.
