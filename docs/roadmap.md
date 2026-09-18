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

M1.1 is implemented with end-to-end mocked automation coverage and live table
diagnosis. Live JSON and actual retry-history checks remain outstanding. The next
implementation slice is M1.2: bounded archive inventory and selected evidence.
Deliver each slice in small tested commits, then update this checklist and capability
evidence. Proposed work below is not currently supported command syntax.

### M1.1 — Run details, stage/job/task timeline and targeted logs (rank 1)

Available: pipeline/run and build details, timeline records with parent IDs, log
index, individual bounded logs and service line ranges. User smoke tests located
a failed build task and read its error. Build commands also accept supported run URLs.
Attempt numbers, stable identifiers, timestamps and safe previous-attempt/sub-timeline
references are now preserved in JSON; tables show order, parent and attempt context.
Optional --include-history traverses referenced timelines within shared bounds.
build diagnose combines build details with categorized outcomes and exact prior-attempt
references. Targeted log content still uses an explicit build log get command.
Missing live coverage: retry-history and JSON/non-interactive operation. Automated
URL-to-diagnosis-to-targeted-log tests now exercise both, including failure paths.

- [x] Accept supported Azure DevOps run URLs directly, including a run link obtained
  from a GitHub check; parse locally, validate host/path/IDs and reject conflicts
  with explicit organization/project context before credential lookup. Do not
  fetch arbitrary links or imply GitHub check discovery is implemented. Implemented
  as --run-url on build inspection/artifact commands; mocked canonical-route and
  no-dispatch rejection coverage, and user-reported live modern-URL timeline read
  for build 18722. Other URL-command combinations remain mock-tested. See [syntax](builds.md).
- [x] Preserve stage/job/task hierarchy, ordering, attempts and available timeline
  references with bounded traversal and explicit missing/incomplete metadata.
  Metadata, table context and bounded --include-history traversal implemented;
  mocked reference identity, cycles, unavailable history and bound coverage passes.
  User verified parent/order and attempt columns on build 18722 (attempt 1, no
  previous references), including --include-history and its timeline-ID column.
  Live traversal across referenced timelines remains outstanding.
- [x] Identify failed tasks and their targeted logs; distinguish failures from
  skipped/cancelled downstream work and earlier retry attempts. Label causal
  interpretation as inference when service evidence cannot establish it.
  build diagnose now reports categories, exact previous-attempt matches and log IDs;
  it does not infer causality or fetch raw logs. Mock coverage passes; the user verified
  live table diagnosis on build 18722 (one failed task at log 21, three failed
  containers, five skipped records). Live retry classification remains pending.
- [x] Verify a URL-to-failure workflow through JSON/non-interactive/read-only mode,
  including missing permission, absent logs, retries and truncated output.
  DiagnosticWorkflowTests follows a run URL through diagnosis with two attempts,
  selects a log ID from JSON and retrieves bounded content. Synthetic stdin tokens,
  exact GET routes, error/partial envelopes and no prompts are verified. 401/403
  timeline failures and 403/404 log failures remain errors. These are mock tests;
  live automation coverage remains a release verification gap.

### M1.2 — Build artifacts and selected evidence (rank 2)

Available: artifact list/get and download of one named artifact to a new ZIP file.
PipelineArtifact download was user-verified; Container live validation is pending.
The user extracted a ZIP externally. ado now inspects local ZIP inventories and
hashes exact selected members; safe extraction is still unimplemented.

- [ ] Add bounded archive inventory and explicit member selection for evidence ZIPs,
  package inventories, manifests and reports. State whether selection reduces
  network transfer or happens after downloading the selected artifact.
  artifact inspect provides local inventory and exact member metadata/hash selection.
  User verified a downloaded artifact inventory and archive hash (build 18522,
  270 entries, limit 100 with truncation warning), then exact selection and hashing
  of CompiledOutputs/src/plugin.json (527 expanded bytes). Opt-in bounded UTF-8
  member text is implemented and user-verified for that manifest with unchanged
  hashes. Structured package interpretation remains open. Selection does not reduce
  download transfer; live JSON/truncated-text checks remain outstanding.
- [ ] Report artifact identity, size and SHA-256; compare an expected hash when
  supplied. A computed digest alone must not be called authenticity verification.
  Local archive/member digests and expected archive hash comparison are implemented;
  remote artifact identity binding and evidence export remain open.
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
| Named org/project profiles and run URLs | Profiles and build --run-url implemented; conflict tests pass; live modern-URL timeline read verified | Broaden URL-command/legacy-host live coverage as authorized; unsupported URL forms remain explicit |
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
