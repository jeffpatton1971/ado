# Capability matrix

Only implemented commands appear in this table. The approved design contains the
architecture; the [requirements backlog](roadmap.md) tracks planned features and
milestone acceptance criteria. Automated tests use mocked HTTP. The user reported successful
live Core project listing/retrieval and pipeline listing/run history; the development
agent has not accessed the live organization or submitted runs.

| Command | Service host | API | Read/write | Pagination | Dry-run | Confirmation | Auth | Status |
|---|---|---|---|---|---|---|---|---|
| --version | Local | None | Read | None | N/A | None | None | Implemented |
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
| auth check | dev.azure.com | Core Projects 7.1 | Read | One list page or project get | N/A | None | Same as list | Tests endpoint access only |
| pipeline list | dev.azure.com | Pipelines 7.1 | Read | Opaque header continuation | N/A | None | vso.build | Mock HTTP verified |
| pipeline get | dev.azure.com | Pipelines 7.1 | Read | None | N/A | None | vso.build | Mock HTTP verified |
| pipeline runs | dev.azure.com | Pipelines Runs 7.1 | Read | No paging; server caps at 10,000 | N/A | None | vso.build | Mock HTTP verified |
| pipeline run get | dev.azure.com | Pipelines Runs 7.1 | Read | None | N/A | None | vso.build | Mock HTTP verified |
| pipeline run start | dev.azure.com | Pipelines Runs 7.1 | Write | None | Local; no credentials/HTTP | Exact action/org/project/ID via --confirm | vso.build_execute | Mock HTTP verified; no live submission |
| pipeline run preview | dev.azure.com | Pipelines Runs 7.1 | POST preview; blocked read-only | None | Local; no credentials/HTTP | Preview-specific exact target | vso.build_execute | Mock HTTP verified; user-reported live success |
| build list | dev.azure.com | Build 7.1 | Read | Opaque header continuation | N/A | None | vso.build | Mock HTTP verified; user-reported live success |
| build get | dev.azure.com | Build 7.1 | Read | None | N/A | None | vso.build | Mock HTTP verified; user-reported live success |
| build diagnose | dev.azure.com | Build and Timeline 7.1 | Read | Bounded timeline scan; optional history | N/A | None | vso.build | Mock verified; user-reported live table success (18722); log references only |
| build timeline | dev.azure.com | Build Timeline 7.1 | Read | Shared record bound; optional bounded --include-history traversal | N/A | None | vso.build | Mock HTTP verified; user-reported live default timeline success (build 18722); traversal live pending |
| build logs | dev.azure.com | Build Logs 7.1 | Read | None; local output bound | N/A | None | vso.build | Mock HTTP verified; user-reported live success |
| build log get | dev.azure.com | Build Logs 7.1 | Read | Optional service line range; no continuation | N/A | None | vso.build | Mock HTTP verified; user-reported live success |
| build artifact list | dev.azure.com | Build Artifacts 7.1 | Read metadata | None; local output bound | N/A | None | vso.build | Mock HTTP verified; user-reported live success |
| build artifact get | dev.azure.com | Build Artifacts 7.1 | Read metadata | None | N/A | None | vso.build | Mock HTTP verified; user-reported live success |
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

Run start supports local previews, exact confirmation, single-attempt POST and uncertain-write
reporting. Server-side YAML preview is separate from local dry-run and requires explicit
--show-yaml to include expanded content. Scope labels above are endpoint documentation identifiers, not Entra
application permissions; Azure DevOps resource authorization is independently required.
