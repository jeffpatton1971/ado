# Capability matrix

Package workflow acceptance tests connect download JSON to local manifest
inspection and evidence export for direct and storage-redirect transfers. They
verify hash agreement and rejection of a wrong expected hash without publication.
Local steps require no credentials/configuration or HTTP. This is synthetic
coverage; user-reported live JSON/non-interactive validation remains pending.

Only implemented commands appear in this table. The approved design contains the
architecture; the [requirements backlog](roadmap.md) tracks planned features and
milestone acceptance criteria. Automated tests use mocked HTTP. The user reported successful
live Core project listing/retrieval and pipeline listing/run history; the development
agent has not accessed the live organization or submitted runs.

| Command | Service host | API | Read/write | Pagination | Dry-run | Confirmation | Auth | Status |
|---|---|---|---|---|---|---|---|---|
| --version | Local | None | Read | None | N/A | None | None | Implemented |
| package inspect | Local | None | Read | One root nuspec, 1 MiB / 10,000 lines | Local read | None | None | Synthetic tests; user verified Json.Input.Provider 1.1.0 identity, dependency declarations and matching hashes; no compatibility/origin verification |
| --help | Local | None | Read | None | N/A | None | None | Implemented |
| config paths | Local | None | Read | None | N/A | None | None | Implemented |
| config show [--effective] | Local | None | Read | None | N/A | None | None | Implemented |
| doctor | Local | None | Read | None | N/A | None | None | Implemented; no token retrieval/network |
| artifact extract | Local | None | Explicit local file write | One exact member; archive/member bounds | Validates/hashes; no writes | Explicit destination; no overwrite | None | Synthetic ZIP verified; user reported successful plugin.json extraction (18522) |
| artifact evidence | Local | None | Explicit local JSON write | One exact member; shared archive bounds | Proposed manifest; no writes | Explicit destination; no overwrite | None | Synthetic ZIP verified; user reported successful plugin.json export (18522); optional origin labels unverified |
| artifact inspect | Local | None | Read | Local entry limit; exact member selection | Local read | None | None | Synthetic ZIP verified; user verified downloaded artifact inventory/archive hash and exact-member hash (18522); no extraction |
| project list | dev.azure.com | Core Projects 7.1 | Read | Numeric continuation header | N/A | None | vso.project; endpoint also documents vso.profile | Mock HTTP verified |
| project get | dev.azure.com | Core Projects 7.1 | Read | None | N/A | None | Same as list | Mock HTTP verified |
| project search | dev.azure.com | Core Projects 7.1 | Read | Bounded list plus client name filter | N/A | None | Same as list | Mock HTTP verified |
| auth check | dev.azure.com / feeds.dev.azure.com | Core / Build / Artifacts 7.1 | Read | Selected project, build, artifact-list or feed probe | N/A | None | Selected endpoint's read scope | Metadata access only; mock-tested; user verified automation feed, build 18722 and artifact-list 18522 probes in read-only/table mode |
| pipeline list | dev.azure.com | Pipelines 7.1 | Read | Opaque header continuation | N/A | None | vso.build | Mock HTTP verified |
| pipeline get | dev.azure.com | Pipelines 7.1 | Read | None | N/A | None | vso.build | Mock HTTP verified |
| pipeline runs | dev.azure.com | Pipelines Runs 7.1 | Read | No paging; server caps at 10,000 | N/A | None | vso.build | Mock HTTP verified |
| pipeline run get | dev.azure.com | Pipelines Runs 7.1 | Read | None | N/A | None | vso.build | Mock HTTP verified |
| pipeline run start | dev.azure.com | Pipelines Runs 7.1 | Write | None | Local; no credentials/HTTP | Exact action/org/project/ID via --confirm | vso.build_execute | Mock HTTP verified; no live submission |
| pipeline run preview | dev.azure.com | Pipelines Runs 7.1 | POST preview; blocked read-only | None | Local; no credentials/HTTP | Preview-specific exact target | vso.build_execute | Mock HTTP verified; user-reported live success |
| build list | dev.azure.com | Build 7.1 | Read | Opaque header continuation | N/A | None | vso.build | Mock HTTP verified; user-reported live success |
| feed list/get | feeds.dev.azure.com | Artifacts Feed Management 7.1 | Read | Local list bound; no documented server paging | N/A | None | vso.packaging | Mock HTTP/CLI tests; project list (testing-upstream) and organization get (automation) verified live |
| package list | feeds.dev.azure.com | Artifacts 7.1 | Read | Bounded numeric offset | N/A | None | vso.packaging | Mock HTTP/CLI; live automation/NuGet listing and bound warning verified; resume pending |
| package versions / version get | feeds.dev.azure.com | Artifacts 7.1 | Read | Local version-list bound / exact GUID | N/A | None | vso.packaging | Mock HTTP/CLI; live pending; name/literal-version lookup uses package resolve |
| package resolve | feeds.dev.azure.com | Artifacts 7.1 | Read | Complete bounded package/version scans required | N/A | None | vso.packaging | Mock verified; live automation/Json.Input.Provider 1.1.0 resolution verified; no ranges or dependency graph |
| package download | feeds.dev.azure.com, pkgs.dev.azure.com and validated storage | Metadata 7.1 / content 7.1-preview.1 | Remote read, explicit local write | Bounded resolution and stream | Local plan, no HTTP/writes | New destination; no overwrite | vso.packaging; storage credentials isolated | Mock verified; user verified automation/Json.Input.Provider 1.1.0 download, matching SHA-256 and external extraction; ZIP-envelope check only |
| build get | dev.azure.com | Build 7.1 | Read | None | N/A | None | vso.build | Mock HTTP verified; user-reported live success |
| build diagnose | dev.azure.com | Build and Timeline 7.1 | Read | Bounded timeline scan; optional history | N/A | None | vso.build | Mock verified; user-reported table and JSON/non-interactive/stdin-token success (18722), complete 30-record scan; log references only; live retry traversal pending |
| build timeline | dev.azure.com | Build Timeline 7.1 | Read | Shared record bound; optional bounded --include-history traversal | N/A | None | vso.build | Mock HTTP verified; user-reported live default timeline success (build 18722); traversal live pending |
| build logs | dev.azure.com | Build Logs 7.1 | Read | None; local output bound | N/A | None | vso.build | Mock HTTP verified; user-reported live success |
| build log get | dev.azure.com | Build Logs 7.1 | Read | Optional service line range; no continuation | N/A | None | vso.build | Mock HTTP verified; user verified table and JSON/non-interactive/stdin-token reads of 18722/log 21; JSON reports complete, untruncated output |
| build artifact list | dev.azure.com | Build Artifacts 7.1 | Read metadata | None; local output bound | N/A | None | vso.build | Mock HTTP verified; user-reported live success |
| build artifact get | dev.azure.com | Build Artifacts 7.1 | Read metadata | None | N/A | None | vso.build | Mock HTTP verified; user-reported live success |
| build artifact evidence | dev.azure.com plus validated storage | Build / Pipelines Artifacts 7.1 | Remote reads; explicit local JSON write | One member; bounded full ZIP download | Local plan; no credentials/HTTP/writes | Explicit destination; no overwrite | vso.build; storage credentials isolated | Mock verified for both types; user reported PipelineArtifact success (18522/17112); Container live pending |
| build artifact download | dev.azure.com plus validated storage redirects | Build / Pipelines Artifacts 7.1 (signedContent for PipelineArtifact) | Remote read, explicit local file write | None; bounded ZIP stream | Local plan, no writes/network | Explicit destination; no overwrite | vso.build; content credentials isolated | Mock verified; user verified PipelineArtifact download and external extraction (build 18522); Container live verification pending |
| release list | vsrm.dev.azure.com | Release 7.1 | Read | Numeric header continuation | N/A | None | vso.release | Mock HTTP verified; user-reported live listing success; resume pending |
| release get | vsrm.dev.azure.com | Release 7.1 | Read | None | N/A | None | vso.release | Mock HTTP verified; user-reported live success (1492) |
| release environments | vsrm.dev.azure.com | Release 7.1 | Read | Local environment bound; no continuation | N/A | None | vso.release | Mock HTTP verified; user-reported live success (1492/1499) |
| release approvals | vsrm.dev.azure.com | Release 7.1 | Read | Local approval bound; no continuation | N/A | None | vso.release | Mock HTTP verified; user-reported live success (1492/3614) |
| release deployments | vsrm.dev.azure.com | Release 7.1 | Read | Local attempt bound; no continuation | N/A | None | vso.release | Mock HTTP verified; user-reported live success (1492/1506) |
| release tasks | vsrm.dev.azure.com | Release 7.1, tasks expansion | Read | Local task bound; no continuation | N/A | None | vso.release | Mock HTTP verified; user-reported live success (1492/1506) |
| release task log | vsrm.dev.azure.com | Release 7.1 | Read | Service line range; local line bound | N/A | None | vso.release | Mock HTTP verified; user-reported live success (1492/1506/task 12) |

Output schema starts at version 1. Azure DevOps Services only.

artifact inspect --show-text reads one exact UTF-8 member up to 1 MiB, with bounded
line output and terminal escaping. It is local-only, opt-in and mock-tested;
the user verified plugin.json text from build 18522. Live JSON/truncated-text
checks remain pending. It does not sanitize arbitrary content.

Build timelines include attempt/identifier/time/reference metadata and parent/order
context. Optional --include-history traverses referenced timelines under shared
record, request, byte and time bounds. Without it, references are not fetched. Missing parents
or unloaded history mark completeness unknown. The user verified parent/order and
attempt columns for build 18722 (attempt 1, zero previous references). Retry-history
and JSON metadata coverage remain mocked. The user also verified --include-history
on build 18722 with one displayed timeline and no previous references; live traversal
across referenced timelines remains pending.

Build get/timeline/logs/log get and artifact list/get/download accept --run-url
for locally validated Azure DevOps results links. Context conflicts fail before
credential lookup. Mocked URL routing/rejection verified; the user verified a live
timeline read using the modern run URL for build 18722. Other URL-command combinations
and the legacy host remain mock-tested only.

Build JSON now includes a nullable `repository` projection (ID, name and type) from
the existing Build response. Build get table output also shows this identity and
the source version. Missing identities remain null/unknown, not inferred from names
or current definitions. Repository URLs, properties and checkout settings are omitted.
Commands embedding BuildInfo, including diagnosis JSON and service-backed artifact
evidence, inherit this additive projection. Reported identity does not verify commit
contents or identify all secondary repositories; repository types retain Build API
spelling and need not match the Pipelines API spelling. The user verified build get
table output for build 18722: repository ID `global-build/rackspace-bat-api`, type
`GitHub`, name unavailable (`unknown`), and the previously observed source SHA.
Live list/JSON identity output and embedded evidence remain unverified.

Pipeline run get includes `repositoryProvenance` in JSON and a repository table.
It allowlists run resource alias, repository type, ref and version from the
[Run Get response](https://learn.microsoft.com/en-us/rest/api/azure/devops/pipelines/runs/get?view=azure-devops-rest-7.1).
Status is `unavailable` when resources/repositories are absent or null,
`no_resources_reported` for an empty map, `versions_unavailable` if any version is
missing/blank, or `reported_versions` when each reported resource has a version.
These statuses describe reported fields, not verified or exhaustive provenance.
Envelope completeness still describes retrieval of the run, not provenance coverage.
Aliases are not global repository IDs; versions are not independently verified.
No current definition, branch HEAD, raw resource URLs, variables or expanded YAML
are fetched or exposed. This does not prove every template revision or checkout,
nor map PR head commits to merge commits. Run listings retain their compact projection.
Repository maps are bounded to 1,000 entries and the existing 4 MiB response bound;
malformed or oversized maps fail rather than silently dropping resources.
Mock HTTP and CLI output tests cover this addition. The user verified run 18722
of pipeline 1128 with `reported_versions`: two gitHub resources (`self` and
`buildAutomationTool`) with refs and exact revisions. Missing-resource states and
other repository types remain mock-tested only; template usage is not established.

Build JSON includes nullable `pullRequestContext`; build get also renders it in table
output. For GitHub/TfsGit, a canonical positive `refs/pull/N/merge` ref supplies an
inferred PR number. Evidence distinguishes `pull_request_reason_and_merge_ref`,
`pull_request_reason_only` and `merge_ref_only`. Only the combined reason/ref with
a nonblank source version labels that version `reported_merge_commit`; other cases
remain `unknown`. No matching evidence produces null context, not a claim that the
build has no PR association. `headVersion` remains null with `not_resolved` status.
This adds context to embedded build diagnostics/evidence too; it does not retrieve
PR details or establish historical head-to-merge mapping. The user verified build get
table context for build 18856: PR 8, `pull_request_reason_and_merge_ref`,
`reported_merge_commit`, and an unknown head version (`not_resolved`). Live JSON
context and the alternative evidence states remain unverified.

Build list `--pr-number` discovers PR validation builds using the documented
`refs/pull/N/merge` source ref and `reasonFilter=pullRequest`. It requires a positive
number plus `--repository-id` and `--repository-type GitHub` or `TfsGit`; this prevents
a PR number from silently spanning repositories. A conflicting `--branch` is rejected
before credential acquisition. Returned rows must match repository ID/type, ref and
reason or the operation fails with `invalid_service_response`. Existing definition,
status, result, SHA and pagination options compose with this filter.
Coverage is limited to this validation trigger/ref convention, not every build
associated with a PR. This does not query GitHub checks or Azure Repos PR details.
`--source-sha` still matches the build's merge commit, not the PR source/head commit.
An empty complete result means no matches in the filtered service listing; it does
not prove a PR has no builds under other conventions. Resume with all original filters.
See [build variables](https://learn.microsoft.com/en-us/azure/devops/pipelines/build/variables?view=azure-devops)
and the Build List API below. The user verified GitHub PR 8 in
`global-build/rackspace-output-terraform`, scoped to definition 1124: build 18856,
completed/succeeded, on `refs/pull/8/merge`. TfsGit and PR continuation remain
mock-tested only; live PR head-to-merge mapping is not implemented.

Build list supports server-side `--repository-id` and `--repository-type` filters
(for example `TfsGit`). `--source-sha` accepts a full 40-character Git SHA and matches
the reported build source version locally, case-insensitively. With this flag,
`--limit` bounds builds scanned, not matching rows. JSON `scannedCount`, completeness
and continuation describe the searched segment; resume with every original filter.
A scan limit can yield zero matches with partial completeness. Missing source versions
make completeness unknown. `--require-complete` returns exit 10 for incomplete searches.
This does not match PR head commits against merge commits or discover template revisions.
The user verified exact-SHA discovery for definition 1126: 71 builds scanned,
one match (18522), complete. Repository filters and partial-search resumption remain
mock-tested only.
See the [Build List API](https://learn.microsoft.com/en-us/rest/api/azure/devops/build/builds/list?view=azure-devops-rest-7.1).

Run start supports local previews, exact confirmation, single-attempt POST and uncertain-write
reporting. Server-side YAML preview is separate from local dry-run and requires explicit
--show-yaml to include expanded content. Scope labels above are endpoint documentation identifiers, not Entra
application permissions; Azure DevOps resource authorization is independently required.
