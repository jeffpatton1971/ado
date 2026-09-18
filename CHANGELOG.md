# Changelog

## Unreleased

- Record user-reported successful modern run-URL timeline read for build 18722;
  the failed golden-request task resolves to log 21 with one error.

- Add --run-url to build inspection and artifact commands, with local Azure DevOps
  results-link parsing, canonical endpoint construction and pre-credential context
  conflict rejection. Supports modern and organization.visualstudio.com links.

- Align milestones with the BAT requirements: authentication and read-only diagnostics,
  selected build evidence, exact source provenance and package inspection form the MVP.
  Add a ranked backlog with current gaps, acceptance criteria and platform release gates;
  defer further classic-release expansion and execution work behind that MVP.

- Record successful user-reported release task log retrieval for release 1492,
  environment 1499, deployment 1506, task 12 after the phaseId parsing fix.

- Fix release task phase parsing to accept the documented decimal-string phaseId;
  retain positive int32 route validation and report invalid phase metadata safely.

- Add release task log with phase resolution from expanded task metadata, bounded
  UTF-8 plain-text reads, optional service line ranges and safe terminal/JSON output.
- Record user-reported live task inspection for release 1492/deployment 1506:
  task 12 (Setting up 200compute) failed; ten task records were returned.
- Add release tasks with expanded deployment task results, attempt/phase/job context,
  bounded output and explicit completeness; task inputs and log URLs are omitted.
- Record user-reported live deployment inspection for release 1492: step 3615,
  deployment 1506, environment 1499, attempt 1, failed/PhaseFailed and started.
- Add release deployments for bounded deployment-attempt summaries with status,
  operation status and started state; no deployment actions or task payloads.
- Record user-reported live approval inspection for release 1492: approval 3614
  was pre-deployment, approved and automated for environment 1499.
- Add read-only release approvals with bounded pre/post-deployment status summaries,
  identity checks, omitted private approval payloads and explicit completeness.
- Record user-reported release environment inspection for release 1492:
  environment 1499 (ib-tasks), definition environment 22, status rejected, rank 1.
- Add release environments with bounded environment IDs, deployment status and rank,
  safe output and strict completeness reporting.
- Record user-reported successful classic release get for release 1492 after
  correcting support for ID-only project references.
- Accept the documented ID-only project reference in classic release responses
  when using a project name; retain explicit name/GUID mismatch checks.
- Record user-reported live classic release listing with a bounded 20-item result
  and truncation warning; individual release retrieval remains unverified.
- Add classic release list/get on the Release service host, with numeric pagination,
  a definition filter, safe metadata output and read-only support.
- Record user-reported successful build timeline retrieval for build 18722,
  locating the failed task at log 21.
- Add build timeline with bounded job/task results, parent IDs, error/warning counts
  and log IDs, credential redaction, terminal escaping and strict completeness.
- Record user-reported successful signed PipelineArtifact download for build 18522,
  CompiledOutputs (4,455,202 bytes), and subsequent PowerShell archive extraction.
- Resolve PipelineArtifact downloads through the Pipelines signedContent API, using
  the build's definition ID and an unexpired URL. The PAT stays on dev.azure.com;
  identity-service redirects remain blocked.
- Allow HTTPS artifact downloads through *.artifacts.visualstudio.com, including the
  user-observed artprodcus3 host, while keeping credentials isolated on every redirect.
- Include a bounded, credential-redacted hostname when an artifact download redirect
  is refused, without exposing signed URLs or changing the storage allowlist.
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
