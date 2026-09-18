# Changelog

## Unreleased

- Add build artifact download with an explicit new-file destination, local dry-run,
  configured byte/time limits, isolated storage redirects, temporary-file cleanup and
  no-overwrite ZIP finalization. No extraction or automatic content retry is performed.
- Record user-reported build-output metadata list/get success for build 18522.

- Add build artifact list/get for bounded output metadata, preserving resource types
  while omitting resource data, property bags and download links. No downloads occur.
- Record user-reported live log index/content success for build 18722/log 3.

- Add build logs and build log get with a bounded log index, optional service line
  ranges, credential redaction, terminal escaping and explicit completeness metadata.
- Record user-reported live build list/get success for definition 1128/build 18722.

- Add build list/get with Build API 7.1, server-side definition/status/result/branch
  filters, newest-queued ordering, bounded opaque pagination and safe execution fields.
- Record user-reported successful live server YAML preview for pipeline 1128.

- Add pipeline run preview with forced previewRun:true, separate exact confirmation,
  local dry-run, read-only guards and optional expanded YAML with known-value redaction.
  Server preview uses one bounded POST and never falls back to run creation.

- Add pipeline run start with a credential-free local dry-run, exact target confirmation,
  optional self-repository ref, typed parameter/variable files, read-only dispatch guards,
  single-attempt POST and non-retryable uncertain-write reporting. No live run was submitted.
- Record user-reported live pipeline listing and run-history verification.

- Add pipeline list/get/runs and pipeline run get using the documented 7.1 Pipelines
  endpoints, project-bound routing, opaque definition continuation and honest run-history
  completeness metadata. Run output omits variables, parameter values and expanded YAML.

- Ignore personal repository-root config.json and document explicit local-config selection.

- Pin development and CI to .NET SDK 10.0.400 without automatic roll-forward.

- Harden redaction of encoded credentials and reject malformed continuation headers;
  ensure every help alias works without configuration or credentials.

- Add CI definitions for three-OS tests, dependency audit, formatting, version checks,
  local package creation and six-RID cross-compilation; publication remains disabled.

- Expose project list/get/search, auth check and local doctor, with explicit credential
  selection, JSON metadata, terminal-safe tables and strict completeness exit behavior.
- Test a disposable synthetic Windows Credential Manager round-trip; no live Azure
  DevOps credentials or requests are used by tests.

- Add registered project endpoints, organization/host validation, redirect refusal,
  bounded read retries and responses, typed project results and continuation handling.

- Add native credential adapters and setup guidance for Windows Credential Manager,
  macOS Keychain and Linux Secret Service, with non-interactive prompt suppression.

- Add opaque PAT/Entra authentication abstractions and bounded environment/stdin/masked-prompt providers (CLI wiring follows).

- Record approved Azure DevOps Services CLI design and threat model.
- Adopt `ado` command name, `ado` configuration directory and `ADO_` environment prefix.
- Add .NET 10 solution, centralized versioning, CLI help/version and safe JSON usage errors.
- Add strict bounded configuration parsing, OS-specific paths, isolated named profiles,
  deterministic overrides, credential-reference validation and configuration diagnostics.
- Preserve JSON envelopes for cancellation and unexpected errors.

No release has been published. Initial development targets version 0.1.0.
