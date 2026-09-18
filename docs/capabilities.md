# Capability matrix

Only implemented commands appear in this table. The approved design contains the
planned service commands. Automated tests use mocked HTTP. The user reported successful
live Core project listing/retrieval and pipeline listing/run history; the development
agent has not accessed the live organization or submitted runs.

| Command | Service host | API | Read/write | Pagination | Dry-run | Confirmation | Auth | Status |
|---|---|---|---|---|---|---|---|---|
| --version | Local | None | Read | None | N/A | None | None | Implemented |
| --help | Local | None | Read | None | N/A | None | None | Implemented |
| config paths | Local | None | Read | None | N/A | None | None | Implemented |
| config show [--effective] | Local | None | Read | None | N/A | None | None | Implemented |
| doctor | Local | None | Read | None | N/A | None | None | Implemented; no token retrieval/network |
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
| build timeline | dev.azure.com | Build Timeline 7.1 | Read | Local bound; sub-timelines not fetched | N/A | None | vso.build | Mock HTTP verified; user-reported live success (build 18722) |
| build logs | dev.azure.com | Build Logs 7.1 | Read | None; local output bound | N/A | None | vso.build | Mock HTTP verified; user-reported live success |
| build log get | dev.azure.com | Build Logs 7.1 | Read | Optional service line range; no continuation | N/A | None | vso.build | Mock HTTP verified; user-reported live success |
| build artifact list | dev.azure.com | Build Artifacts 7.1 | Read metadata | None; local output bound | N/A | None | vso.build | Mock HTTP verified; user-reported live success |
| build artifact get | dev.azure.com | Build Artifacts 7.1 | Read metadata | None | N/A | None | vso.build | Mock HTTP verified; user-reported live success |
| build artifact download | dev.azure.com plus validated storage redirects | Build / Pipelines Artifacts 7.1 (signedContent for PipelineArtifact) | Remote read, explicit local file write | None; bounded ZIP stream | Local plan, no writes/network | Explicit destination; no overwrite | vso.build; content credentials isolated | Mock verified; user verified PipelineArtifact download and external extraction (build 18522); Container live verification pending |
| release list | vsrm.dev.azure.com | Release 7.1 | Read | Numeric header continuation | N/A | None | vso.release | Mock HTTP verified; user-reported live listing success; resume pending |
| release get | vsrm.dev.azure.com | Release 7.1 | Read | None | N/A | None | vso.release | Mock HTTP verified; user-reported live success (1492) |
| release environments | vsrm.dev.azure.com | Release 7.1 | Read | Local environment bound; no continuation | N/A | None | vso.release | Mock HTTP verified; user-reported live success (1492/1499) |
| release approvals | vsrm.dev.azure.com | Release 7.1 | Read | Local approval bound; no continuation | N/A | None | vso.release | Mock HTTP verified; user-reported live success (1492/3614) |
| release deployments | vsrm.dev.azure.com | Release 7.1 | Read | Local attempt bound; no continuation | N/A | None | vso.release | Mock HTTP verified; live pending |

Output schema starts at version 1. Azure DevOps Services only.

Run start supports local previews, exact confirmation, single-attempt POST and uncertain-write
reporting. Server-side YAML preview is separate from local dry-run and requires explicit
--show-yaml to include expanded content. Scope labels above are endpoint documentation identifiers, not Entra
application permissions; Azure DevOps resource authorization is independently required.
