# Changelog

## Unreleased

- Add a low-priority post-M1 roadmap option for Entra-authenticated, read-only PAT
  expiry inspection; no token lifecycle command is implemented by this change.

## 1.0.0

Initial stable-version candidate for the standalone `ado` Azure DevOps Services CLI.
Tagging and publication are separate release actions, pending candidate verification.

### Features

- Named organization/project profiles, local configuration diagnostics and scoped
  project/build/artifact/feed access checks.
- PAT and externally acquired Entra Bearer tokens through environment, stdin,
  masked prompts, Windows Credential Manager, macOS Keychain and Linux Secret Service.
- Project and pipeline discovery, run details and repository resource revisions.
- Build discovery by pipeline, repository, branch, exact source SHA and supported
  PR merge refs, with bounded scans and explicit incomplete/unknown results.
- Run-URL diagnostics, stage/job/task timelines, bounded attempt-history traversal,
  failed-task findings and targeted log reads with optional line ranges.
- PipelineArtifact and Container downloads; bounded ZIP inventories, selected-member
  text/hash inspection, safe extraction and local or service-backed evidence manifests.
- Organization/project feeds, package/version inventories, exact NuGet resolution,
  bounded downloads and local nuspec identity/dependency declaration inspection.
- Read-only classic release, environment, approval, deployment, task and log inspection.
- Explicit pipeline run start and server YAML preview with local dry-runs, exact
  confirmation and centralized read-only guards.

### Safety and automation

- Versioned JSON envelopes, non-interactive operation, stable exit codes,
  cancellation, endpoint-specific pagination and explicit completeness metadata.
- No token persistence, generic unrestricted HTTP command or execution of service
  content. Validated service routes and isolated artifact redirects protect credentials.
- Bounded archives and transfers, unsafe-path/link/collision rejection, expected
  SHA-256 checks, no-overwrite destinations and temporary-output cleanup.
- Mutations are never blindly retried after ambiguous failures. Evidence reports
  observed outcomes and byte identities without claiming authenticity or causality.

### Platforms and verification

- Windows, macOS and Linux support with exact .NET SDK 10.0.400 for development.
- Three-OS CI builds, tests, formatting, dependency audit and isolated tool-install
  checks; six Windows/Linux/macOS x64/Arm64 self-contained cross-compilation targets.
- Live Windows/macOS usage; user-verified diagnostics, both artifact types, package
  download/inspection and exact Core, YAML and JSON package resolution.
- Full local verification and historical hosted results are detailed in docs/testing.md.

### Known limitations

- Azure DevOps Services only; no Azure DevOps Server/on-premises compatibility.
- Entra acquisition/refresh is external. Live Entra access and native Linux Secret
  Service are unverified; cross-compilation does not prove every architecture at runtime.
- Retry-history traversal has synthetic coverage but no available live retry fixture.
- PR head-to-merge mappings and complete template/checkout provenance are unresolved
  where service evidence is unavailable. Current branch HEAD is never substituted.
- Package ranges, dependency closure, signatures and assembly compatibility are not
  evaluated. Selective artifact inspection follows a bounded full artifact download.
- Package metadata and content reads are separate operations, not a transactional snapshot.
