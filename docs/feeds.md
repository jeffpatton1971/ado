# Azure Artifacts feeds

`ado feed list` and `ado feed get --feed NAME_OR_ID` read feed identities.
The default `--scope project` uses the selected profile/environment/flag project.
Use `--scope organization` to omit that project from the route, even when configured.
Organization listing can return both organization- and project-associated feeds;
each row retains its reported scope and project identity. To get a project feed,
use project scope with its project; use organization scope for an organization feed.
Views (`feed@view`) are not supported in this slice.

```powershell
dotnet run --project src/Ado.Cli --configuration Release -- feed list --config ./config.json --token-prompt --output table --read-only --limit 100
dotnet run --project src/Ado.Cli --configuration Release -- feed list --scope organization --config ./config.json --token-prompt --output table --read-only --limit 100
```

List/get return only feed ID/name, reported scope and project ID/name. URLs, upstream
definitions, permissions and personal identities are omitted. The selected credential
is redacted; arbitrary names are not guaranteed secret-free. No package is downloaded
and no remote mutation is performed. `--dry-run` does not suppress these read requests.

The API has no documented paging parameters. `--limit` bounds locally returned rows;
`--all` uses the configured maximum. The response is bounded to 4 MiB and 10,000 feed
entries; oversized or malformed responses fail. All returned entries are validated,
including entries beyond the display limit. Duplicate feed IDs and unexpected
continuation headers fail rather than implying completeness. No `--top` or
`--continuation-token` is accepted. Local truncation reports `item_limit`, partial
completeness and scanned count; `--require-complete` retains data and returns exit 10.
Raise the bounded limit to retrieve omitted rows; there is no resumable cursor.

Completeness describes the service's visible listing, not every feed in the organization.
An empty listing does not prove nonexistence. HTTP authentication/permission/not-found
responses retain the CLI's error categories; a service-masked 404 cannot distinguish
missing access from a missing feed. The documented read scope is `vso.packaging`.
Native credentials, external Entra tokens, cancellation and JSON/non-interactive behavior
use the existing transport and authentication path. The user verified project list
for rseng/impldevmpc (`testing-upstream`). The user's `automation` feed is organization
scoped, so select `--scope organization` to retrieve it. Organization listing and
feed get remain pending live verification.

References: [list feeds](https://learn.microsoft.com/en-us/rest/api/azure/devops/artifacts/feed-management/get-feeds?view=azure-devops-rest-7.1),
[get feed](https://learn.microsoft.com/en-us/rest/api/azure/devops/artifacts/feed-management/get-feed?view=azure-devops-rest-7.1).
