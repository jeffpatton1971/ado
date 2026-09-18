# Requirements and delivery backlog

Aligned on 2026-09-17 to the user's BAT requirements sheet. This replaces the
service-by-service milestone order in the original design. The first release is
authenticated, read-only pipeline diagnostics: authentication plus ranks 1–4.
It must let a human or AI follow a run link, investigate a failure, and retrieve
selected evidence without requiring copied logs or deployment authority.

Status distinguishes implemented primitives from completed workflows. User-reported
live checks demonstrate specific paths only; mocks do not prove service access.
See [capabilities](capabilities.md) for shipped commands and [testing](testing.md)
for platform and verification limits. This backlog grants no live access or writes.

## Milestones

| Milestone | Scope | Completion gate |
|---|---|---|
| M1 — Read-only diagnostics MVP | Authentication, day-one requirements, ranks 1–4 | All M1 checklist items complete; acceptance scenarios below verified with explicit coverage and limitations |
| M2 — Configuration and deeper evidence | Ranks 5–8 | Definition inspection, non-executing preview, test comparison, bounded watching and richer evidence export |
| M3 — Operational diagnostics | Ranks 9–10 | Read-only agent/queue and deployment-wait inspection |
| M4 — Optional execution | Rank 11 | Separately scoped, explicitly authorized run/retry/cancel with uncertain-write reconciliation |
| Deferred | Rank 12, Azure Repos PR management, Boards CRUD | Outside first-version scope; GitHub and Jira cover the latter workflows |

Platform validation, packaging, documentation and CI are release gates throughout,
not a substitute for finishing M1. Existing run-start, preview and classic-release
commands remain available, but their presence does not make M1 complete. Further
classic-release expansion and mutation work follow the priority order below.

## M1: authentication and ranks 1–4

The active slice is M1.1. Run-URL input is implemented; retry-aware diagnostics are next.
Deliver each slice in small tested commits, then update this checklist and capability
evidence. Proposed work below is not currently supported command syntax.

### M1.1 — Run details, stage/job/task timeline and targeted logs (rank 1)

Available: pipeline/run and build details, timeline records with parent IDs, log
index, individual bounded logs and service line ranges. User smoke tests located
a failed build task and read its error. Build commands also accept supported run URLs.
Missing: richer attempt context, sub-timeline traversal and a consolidated diagnostic workflow.

- [x] Accept supported Azure DevOps run URLs directly, including a run link obtained
  from a GitHub check; parse locally, validate host/path/IDs and reject conflicts
  with explicit organization/project context before credential lookup. Do not
  fetch arbitrary links or imply GitHub check discovery is implemented. Implemented
  as --run-url on build inspection/artifact commands; mocked canonical-route and
  no-dispatch rejection coverage, live URL smoke test pending. See [syntax](builds.md).
- [ ] Preserve stage/job/task hierarchy, ordering, attempts and available timeline
  references with bounded traversal and explicit missing/incomplete metadata.
- [ ] Identify failed tasks and their targeted logs; distinguish failures from
  skipped/cancelled downstream work and earlier retry attempts. Label causal
  interpretation as inference when service evidence cannot establish it.
- [ ] Verify a URL-to-failure workflow through JSON/non-interactive/read-only mode,
  including missing permission, absent logs, retries and truncated output.

### M1.2 — Build artifacts and selected evidence (rank 2)

Available: artifact list/get and download of one named artifact to a new ZIP file.
PipelineArtifact download was user-verified; Container live validation is pending.
The user extracted a ZIP externally; ado itself does not inspect or extract archives.

- [ ] Add bounded archive inventory and explicit member selection for evidence ZIPs,
  package inventories, manifests and reports. State whether selection reduces
  network transfer or happens after downloading the selected artifact.
- [ ] Report artifact identity, size and SHA-256; compare an expected hash when
  supplied. A computed digest alone must not be called authenticity verification.
- [ ] Add opt-in safe extraction: reject path traversal, absolute paths, unsafe
  links, duplicate/colliding paths and existing destinations; bound entry count,
  expanded bytes and execution time; clean up interrupted temporary output.
- [ ] Produce a minimal sanitized evidence manifest identifying the selected
  run/artifact/files, digests and limitations. No automatic raw-log retention.
- [ ] Validate both supported artifact types and failure/cleanup cases, recording
  live versus mocked coverage separately.

### M1.3 — Run discovery and exact source provenance (rank 3)

Available: pipeline run history; build filters for definition, branch, status and
result; build JSON includes source branch and source version. These do not establish
all repository, PR or shared-template revisions used by a run.

- [ ] Discover runs by repository, branch, exact SHA, PR and pipeline, using
  documented endpoint capabilities and bounded client filtering where necessary.
  Report scan completeness; never imply an unbounded search was exhaustive.
- [ ] Report repository identity, exact source commit, PR/merge context and resolved
  repository resources/shared-template revisions where the service exposes them.
- [ ] Distinguish run-time revisions from current definition or branch contents;
  mark unavailable provenance unknown rather than substituting current HEAD.
- [ ] Verify that similarly named successful runs, different commits and retry
  attempts cannot silently satisfy an exact-source query.

### M1.4 — Azure Artifacts feeds and packages (rank 4)

No feed/package CLI commands are implemented yet.

- [ ] List/get feeds, packages and exact versions, supporting applicable organization
  and project scopes, pagination and explicit permissions/completeness diagnostics.
- [ ] Resolve Core, YAML, JSON and other requested dependency versions to exact
  package identities; distinguish not found, inaccessible and incomplete searches.
- [ ] Download an exact NuGet package with credential isolation, explicit destination,
  byte/time bounds, no overwrite and reported digest.
- [ ] Inspect selected .nuspec, manifest and assembly entries using the bounded
  archive facilities from M1.2; never execute downloaded code.
- [ ] Keep preview-only provenance features explicitly opted in and separate from
  basic package availability. Record scope/API and verification limits.

## M1: requirements from day one

| Requirement | Current evidence | Remaining acceptance work |
|---|---|---|
| Cross-platform .NET; PowerShell-friendly | SDK pinned to 10.0.400; Windows x64 tests; three-OS CI authored | Execute target-OS checks, native-store checks and installation smoke tests; distinguish cross-compilation from runtime coverage |
| Named org/project profiles and run URLs | Profiles and build --run-url implemented; conflict tests pass | Live URL smoke test; unsupported URL forms remain explicit |
| Authentication diagnostics | Credential providers and HTTP error categories; auth check probes projects | Add service-specific read probes for build/artifact/feed access; retain ambiguity for masked not-found/public access; distinguish network, credential, permission and resource failures where possible |
| OS PAT stores and Entra | Native adapters and externally supplied Entra tokens; Windows synthetic store test | Validate macOS/Linux stores; document token acquisition/refresh limitations; do not claim automatic Entra login |
| Never print/persist tokens | Selected credential redaction, safe config output, no CLI credential persistence; plaintext config rejected | Preserve rejection of plaintext credentials; verify secret handling across new commands and exports; unknown secrets in service logs are not automatically sanitized |
| Stable automation contract | JSON envelope, non-interactive mode, bounded reads, cancellation, exit codes | Apply contracts to every new workflow; test partial results, cancellation and endpoint-specific continuation |
| Read-only and explicit writes | Dispatch safeguards, exact run confirmation, no automatic write retry | Preserve safeguards; read-only diagnostics must never queue/retry/cancel or approve a deployment |
| No raw-log retention; sanitized evidence; safe ZIP handling | Logs printed on request; explicit ZIP download, no overwrite; no built-in extraction/export | Complete M1.2 with allowlisted summary fields and opt-in files; never label arbitrary log/artifact contents sanitized |

M1 acceptance scenarios: starting from a supported run URL and selected credential
profile, obtain exact run/source context, locate a failing task and bounded log
range, retrieve selected artifact evidence with digests, and check/download an exact
dependency package. Demonstrate deterministic JSON without prompts, explicit
incompleteness and no remote mutations. Use synthetic fixtures for negative cases
and separately authorized user smoke tests for live paths. Document unavailable
permissions and data; neither corporate restrictions nor VPN-only portal access
are bypassed by this CLI.

## M2: need it next

| Rank | Feature and current status | Completion criteria |
|---|---|---|
| 5 | Definition/configuration inspection: basic pipeline metadata only | Inspect YAML location, branch filters, defaults, shared templates and accessible non-secret UI settings; distinguish current configuration from what a run used and avoid unconditional publication predictions |
| 6 | Expanded YAML: preview implemented and user-verified | Review endpoint/permissions and safe preview semantics; inspect assembled YAML and substitutions without execution; document that preview success validates neither execution nor deployment. Current command requires confirmation and is blocked in read-only mode |
| 7 | Detailed tests/comparison: absent | Failed assertions, counts, skipped/not-run tests, bounded attachments and comparison to an explicitly identified last successful run; do not treat missing artifact tests as passed |
| 8 | Watching/export: absent beyond planned M1 manifest | Bounded polling of an already-authorized run with cancellation; sanitized consolidated export of run ID, source SHA, outcomes, artifact identities and limitations; no automatic raw logs |

## M3 and later: can wait

| Rank | Feature | Scope |
|---|---|---|
| 9 | Agent pools/queues | Offline agents, unmet demands, queue delays and allowlisted non-secret capabilities |
| 10 | Environments/checks/approvals | Explain deployment waits read-only; classic-release environment/approval/deployment/task reads already exist, but YAML environment checks are distinct and not implemented |
| 11 | Run/retry/cancel | Run start exists; retry/cancel do not. All require separate explicit authorization, since execution may publish or deploy |
| 12 | Administrative writes | Definition/service-connection/permission/variable-group/retention edits, package promotion/deletion and deployment approval are outside the first version |

## Tracking rules

Check off items only with implementation and relevant validation evidence. Update
this backlog, capability matrix and changelog in each feature commit. Keep live
coverage separate from mocks; do not mark a milestone complete solely because its
commands exist. Packaging and publication remain separately authorized actions.
