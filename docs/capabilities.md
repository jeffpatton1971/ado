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
| pipeline run preview | dev.azure.com | Pipelines Runs 7.1 | POST preview; blocked read-only | None | Local; no credentials/HTTP | Preview-specific exact target | vso.build_execute | Mock HTTP verified; live preview pending |

Output schema starts at version 1. Azure DevOps Services only.

Run start supports local previews, exact confirmation, single-attempt POST and uncertain-write
reporting. Server-side YAML preview is separate from local dry-run and requires explicit
--show-yaml to include expanded content. Scope labels above are endpoint documentation identifiers, not Entra
application permissions; Azure DevOps resource authorization is independently required.
