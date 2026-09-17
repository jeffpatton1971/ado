# Capability matrix

Only implemented commands appear in this table. The approved design contains the
planned service commands. Implemented APIs are tested with mocked HTTP; no live
organization access has been performed during development.

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

Output schema starts at version 1. Azure DevOps Services only.

No remote mutations exist yet. Read-only and dry-run dispatch guards are present;
mutation previews, exact confirmation and uncertain-write behavior await mutation
implementation. Scope labels above are endpoint documentation identifiers, not Entra
application permissions; Azure DevOps resource authorization is independently required.
