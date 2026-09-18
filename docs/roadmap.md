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

Current focus: final M1 release-readiness review. The ranks 1–4 feature checklists
are complete with synthetic coverage and user-verified primary live paths, including
Core, YAML and JSON package resolution. Before declaring M1 complete, reconcile
day-one platform/automation evidence and documented limitations, run final local
verification and prepare the candidate version/changelog for human release review.
Unavailable live retry history remains a documented, non-blocking limitation.
The selected initial stable version is 1.0.0. Directory.Build.props, package
references and installation examples use that version; CHANGELOG.md summarizes
its scope and limitations. Candidate CI is required before tagging/publication.
The user accepted Windows, macOS and Linux support on 2026-09-18: live use on
Windows/macOS plus hosted Linux CI is sufficient for M1 platform acceptance.
Unverified Linux native-store and architecture-specific runtime cases remain
documented coverage limits, not blockers requiring additional user hardware.
The user completed the full local verification sequence for 0.1.0 after ca46b76:
locked restore, Release build, 563 passing tests/one skip, formatting, packing and
installed-tool smoke checks. Candidate hosted CI and final version/changelog review
remain; earlier cross-platform CI is not attributed to this newer source state.
Deliver each slice in small tested commits, then update this checklist and capability
evidence. Unimplemented checklist work below is not supported command syntax.

The user selected completion of M1 (the first group, ranks 1–4 plus day-one gates)
as the first tagged-version checkpoint. Announce when the gates are met and report
the candidate version/source state for human review. Under [AGENTS.md](../AGENTS.md),
only a human may create/push a tag or publish a release; agents must not do so.
Keep any unavailable live/platform checks explicit rather than silently checking
them off. Verification CI and local packaging do not authorize a release.

Live retry-history validation is unavailable: the user could not identify an
existing run with a retried job/stage. Retain synthetic attempt-history coverage
and document this release limitation; it does not independently block M1. Do not
rerun a work pipeline solely to manufacture a fixture. Standalone package version
listing and exact-version retrieval are now user-verified for the organization-scoped
automation package Json.Input.Provider. Package listing also passed live JSON,
stdin-token/non-interactive pagination: two distinct pages advanced offsets 2 to 4.

### M1.1 — Run details, stage/job/task timeline and targeted logs (rank 1)

Available: pipeline/run and build details, timeline records with parent IDs, log
index, individual bounded logs and service line ranges. User smoke tests located
a failed build task and read its error. Build commands also accept supported run URLs.
Attempt numbers, stable identifiers, timestamps and safe previous-attempt/sub-timeline
references are now preserved in JSON; tables show order, parent and attempt context.
Optional --include-history traverses referenced timelines within shared bounds.
build diagnose combines build details with categorized outcomes and exact prior-attempt
references. Targeted log content still uses an explicit build log get command.
User verified JSON/non-interactive diagnosis with stdin credentials for build 18722:
30 records, complete schema-v1 envelope and failed-task log reference 21.
User then retrieved log 21 in JSON/non-interactive/read-only mode with stdin credentials
and --require-complete; the response reported complete, untruncated output.
Missing live coverage: retry-history traversal and the combined run-URL automation path. Automated
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
  live JSON/non-interactive diagnosis by build ID is now user-verified for 18722.
  User also verified the selected log 21 read in JSON/non-interactive mode, completing
  the diagnosis-to-log sequence by build ID. Live run-URL automation and retry
  traversal remain verification gaps.

### M1.2 — Build artifacts and selected evidence (rank 2)

Available: artifact list/get and download of one named artifact to a new ZIP file.
PipelineArtifact download was user-verified. Container metadata listing is now
user-verified for build 7826, artifact drop (5163), as are its 654-byte ZIP download,
two-entry inventory/archive hash and external extraction. Selected-member hashing
and Container service-backed evidence also passed, with both digests matching.
The user extracted a ZIP externally. ado now inspects local ZIP inventories and
hashes exact selected members; single-member extraction to an explicit new file
is implemented with shared validation and no-overwrite publication.

- [x] Add bounded archive inventory and explicit member selection for evidence ZIPs,
  package inventories, manifests and reports. State whether selection reduces
  network transfer or happens after downloading the selected artifact.
  artifact inspect provides local inventory and exact member metadata/hash selection.
  User verified a downloaded artifact inventory and archive hash (build 18522,
  270 entries, limit 100 with truncation warning), then exact selection and hashing
  of CompiledOutputs/src/plugin.json (527 expanded bytes). Opt-in bounded UTF-8
  member text is implemented and user-verified for that manifest with unchanged
  hashes. Structured nuspec declarations are implemented and user-verified. Selection does not reduce
  download transfer; live JSON/truncated-text checks remain outstanding.
- [x] Report artifact identity, size and SHA-256; compare an expected hash when
  supplied. A computed digest alone must not be called authenticity verification.
  Local archive/member digests and expected archive hash comparison are implemented;
  local evidence export is implemented. build artifact evidence now records scoped
  authenticated metadata plus hashes of content downloaded during the operation;
  mock coverage passes for both types. The user verified PipelineArtifact export for
  build 18522/artifact 17112, including source revision and matching member digest.
  ZIP digest differed from the earlier download; the cause was not established.
- [x] Add opt-in safe extraction: reject path traversal, absolute paths, unsafe
  links, duplicate/colliding paths and existing destinations; bound entry count,
  expanded bytes and execution time; clean up interrupted temporary output.
  artifact extract now writes one exact member to an explicit destination, with
  full archive validation, shared bounds, temporary cleanup and no overwrite.
  Mock coverage passes; the user reported successful plugin.json extraction from
  build 18522 (527 bytes, matching prior digests, written:true). Broader platform
  validation and live failure-path/dry-run checks remain open.
  Bulk directory-tree extraction is not implemented.
- [x] Produce a minimal sanitized evidence manifest identifying the selected
  run/artifact/files, digests and limitations. No automatic raw-log retention.
  artifact evidence exports allowlisted local archive/member metadata and hashes,
  with no raw contents or absolute input paths. Optional run/artifact labels are
  explicitly user_supplied_unverified. The user reported a successful local export
  for plugin.json from build 18522 with matching hashes and no origin labels.
  build artifact evidence records service-observed build and artifact identities
  alongside downloaded hashes, without claiming transactional or publisher provenance.
  PipelineArtifact export is user-verified for build 18522; Container export is
  user-verified for build 7826/drop, selecting drop/20210113.1.json. Names/labels
  are not claimed secret-free.
- [x] Validate both supported artifact types and failure/cleanup cases, recording
  live versus mocked coverage separately.
  The preceding implementation items have synthetic tests and primary live-path
  evidence: downloads, inventories and service-backed evidence passed for both
  PipelineArtifact and Container. Failure/cleanup cases use synthetic tests, with
  hosted Windows/Linux/macOS execution. Remaining live negative cases and broader
  platform limits are documented separately; no live failure injection is claimed.

### M1.3 — Run discovery and exact source provenance (rank 3)

Available: pipeline run history; build filters for definition, branch, status,
result and repository ID/type; bounded exact source SHA filtering with scan counts
and resumable continuation. Build JSON includes source branch, source version and
service-reported repository ID/name/type. Build get tables show identity and version.
These do not establish
all repository, PR or shared-template revisions used by a run. Pipeline run get now
reports the run's repository resource aliases, types, refs and resolved versions,
with explicit unavailable states; it does not infer template usage or fetch current HEAD.

- [x] Discover runs by repository, branch, exact SHA, PR and pipeline, using
  documented endpoint capabilities and bounded client filtering where necessary.
  Report scan completeness; never imply an unbounded search was exhaustive.
  Repository-scoped `--pr-number` now covers GitHub/TfsGit PR validation builds
  using `refs/pull/N/merge` and the pullRequest reason. Other provider/ref conventions
  and PR head-to-merge mapping are documented unsupported cases for M1. Live GitHub PR discovery was
  verified for global-build/rackspace-output-terraform PR 8 (definition 1124,
  build 18856); TfsGit and PR pagination remain mock-tested only.
- [x] Report repository identity, exact source commit, PR/merge context and resolved
  repository resources/shared-template revisions where the service exposes them.
  Build PR context now distinguishes trigger reason and merge-ref inference, labels
  reported merge revisions and leaves historical PR head revisions unresolved.
  Live build 18856 verifies repository/merge context; run 18722 reports self and
  buildAutomationTool resource revisions. Synthetic tests preserve unavailable
  versions and avoid current-branch reads. Resource revisions do not prove every
  template was used; unavailable template/checkout mappings remain unknown.
- [x] Distinguish run-time revisions from current definition or branch contents;
  mark unavailable provenance unknown rather than substituting current HEAD.
  RunRepositoryProvenanceTests verifies reported resources and unavailable states
  without current-branch requests; user verified reported resources for run 18722.
- [x] Verify that similarly named successful runs, different commits and retry
  attempts cannot silently satisfy an exact-source query.
  Ordinary repository filters now validate returned repository ID/type as PR queries
  already did. Regression cases reject wrong/missing identities even with the
  requested SHA and a successful result. Existing tests cover SHA mismatch, unknown
  source versions, bounded continuation and PR head-versus-merge distinctions.
  New two-page acceptance cases use identical pipeline/build names: successful
  different-SHA builds are excluded, while failed and successful runs of the exact
  SHA retain distinct build IDs and outcomes. A bounded scan stays partial and
  resumable instead of substituting a successful match. Timeline/history and
  diagnostic workflow tests separately preserve exact attempt references; live
  retry-history validation remains open under M1.1.

### M1.4 — Azure Artifacts feeds and packages (rank 4)

Feed list/get is implemented for explicit project or organization scope, with bounded
output and allowlisted identity fields. Project feed list is verified live for
rseng/impldevmpc (`testing-upstream`); organization-scoped get is verified for
rseng's `automation` feed. Organization listing, project get and live JSON remain pending.
Package listing, bounded version inventory and exact version-GUID retrieval are
implemented. NuGet listing in automation is verified live (20 rows and a bound
warning); JSON/non-interactive continuation is user-verified across two distinct
two-item pages, advancing offsets 2 to 4 without claiming exhaustion.
Standalone version listing returned eight
visible versions with --require-complete and no truncation warning; exact version
retrieval returned the known 1.1.0 GUID. Exact NuGet name/literal
version resolution to service GUIDs is now implemented with bounded complete scans;
live resolution is verified for automation's Json.Input.Provider 1.1.0. Exact NuGet
download now resolves metadata then saves bounded content with SHA-256 and isolated
storage redirects; user verified Json.Input.Provider 1.1.0 download and matching digest. Structured local package inspection
is implemented and user-verified for Json.Input.Provider 1.1.0. Version-range/dependency-graph solving is outside this exact
version lookup and is not an additional M1 acceptance gate.

- [x] List/get feeds, packages and exact versions, supporting applicable organization
  and project scopes, pagination and explicit permissions/completeness diagnostics.
  Scoped feed/package contract tests cover both route forms, errors and bounds.
  Live organization package listing/resumption, version list/get and exact resolution
  complement project feed listing and organization feed retrieval. Remaining live
  scope/format combinations are coverage limits, not missing command implementations.
- [x] Resolve Core, YAML, JSON and other requested dependency versions to exact
  package identities; distinguish not found, inaccessible and incomplete searches.
  JSON 1.1.0 and Rackspace.BAT.Core.Abstractions 2.8.0 are user-verified in
  automation. Core resolution with --limit 100 refused incomplete version evidence;
  --all then resolved the exact version. YAML name discovery and version inventory
  are user-verified for Yaml.Input.Provider; 2.5.0 is reported listed/latest.
  Exact Yaml.Input.Provider 2.5.0 resolution with --all subsequently returned the
  expected package/version IDs. This acceptance fixture does not select or change
  downstream dependencies. Synthetic tests cover not-found, inaccessible and
  incomplete outcomes. Resolution is metadata evidence, not download permission.
- [x] Download an exact NuGet package with credential isolation, explicit destination,
  byte/time bounds, no overwrite and reported digest.
  Implemented as package download using the documented 7.1-preview.1 content route,
  with local dry-run and ZIP-envelope checks. Mock transfer/failure tests pass;
  user verified organization-scoped automation download of Json.Input.Provider 1.1.0
  (18,092 bytes), matching the previously inspected archive SHA-256. External
  PowerShell extraction succeeded. Destination-exists refusal was also verified.
  Nuspec/signature checks are separate; this does not prove publisher authenticity.
- [x] Inspect selected .nuspec, manifest and assembly entries using the bounded
  archive facilities from M1.2; never execute downloaded code.
  Local nuspec text/hash inspection is user-verified for Json.Input.Provider 1.1.0
  (18,092-byte archive, 9 entries). package inspect reports identity, dependency
  groups and raw version declarations with archive/member hashes; synthetic tests
  pass. User verified structured identity, net9.0 dependency declarations and both
  hashes matching the earlier local inspection. Declarations do not establish compatibility.
  Synthetic JSON/non-interactive workflows now connect direct/redirected package
  downloads to inspection and evidence export, checking hash agreement, local-only
  reads, metadata-only evidence and refusal to publish on expected-hash mismatch.
  A synthetic package additionally verifies exact manifest and binary DLL byte/hash
  selection through JSON without config or HTTP access. Assembly inspection is
  byte-level only; no assembly loading, execution or API/compatibility analysis.
- [x] Keep preview-only provenance features explicitly opted in and separate from
  basic package availability. Record scope/API and verification limits.
  No package provenance endpoint is implemented or called implicitly. Metadata
  reads use Artifacts 7.1; explicitly requested content download uses 7.1-preview.1
  and does not claim publisher/build provenance. This item does not require adding
  an optional preview provenance command to M1.

## M1: requirements from day one

| Requirement | Current evidence | Remaining acceptance work |
|---|---|---|
| Cross-platform .NET; PowerShell-friendly | SDK 10.0.400; hosted Windows x64/Linux x64/macOS Arm64 tests and installed-tool checks passed; six RID cross-compiles passed in [run 35360735378](https://github.com/jeffpatton1971/ado/actions/runs/35360735378) | Remaining native-store and Windows/Linux Arm64 runtime checks; macOS x64 only cross-compiled; distinguish compilation from runtime coverage |
| Named org/project profiles and run URLs | Profiles and build --run-url implemented; conflict tests pass; live modern-URL timeline read verified | Broaden URL-command/legacy-host live coverage as authorized; unsupported URL forms remain explicit |
| Authentication diagnostics | Credential providers and HTTP error categories; scoped GET and error-path tests pass; user verified automation feed, build 18722 and artifact-list 18522 probes | Live JSON/non-interactive verification pending; retain ambiguity for masked not-found/public access; probes do not establish content or write permissions |
| OS PAT stores and Entra | Native adapters and externally supplied Entra tokens; Windows synthetic store test; user-verified macOS installation/Keychain auth, non-interactive and repeated checks | Linux native Secret Service and macOS native failure/re-authorization paths unverified; document token acquisition/refresh limitations; do not claim automatic Entra login |
| Never print/persist tokens | Selected credential redaction, safe config output, no CLI credential persistence; plaintext config rejected | Preserve rejection of plaintext credentials; verify secret handling across new commands and exports; unknown secrets in service logs are not automatically sanitized |
| Stable automation contract | JSON envelope, non-interactive mode, bounded reads, cancellation, exit codes | Apply contracts to every new workflow; test partial results, cancellation and endpoint-specific continuation |
| Read-only and explicit writes | Dispatch safeguards, exact run confirmation, no automatic write retry | Preserve safeguards; read-only diagnostics must never queue/retry/cancel or approve a deployment |
| No raw-log retention; sanitized evidence; safe ZIP handling | Logs printed on request; bounded ZIP inspection, exact-member extraction and local/service-backed evidence export; explicit destinations with no overwrite | Finish artifact/platform acceptance coverage; never label arbitrary log/artifact contents sanitized |

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
| 8 | Watching: absent; selected artifact evidence export exists | Bounded polling of an already-authorized run with cancellation; sanitized consolidated export of run ID, source SHA, outcomes, artifact identities and limitations; no automatic raw logs |

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
