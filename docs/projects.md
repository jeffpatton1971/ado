# Project discovery and diagnostics

Use the source-built executable, or substitute `dotnet run --project src/Ado.Cli --`
for `ado` in these examples. No release is published yet. Examples access Azure DevOps
when you execute them; development tests use fake HTTP handlers only.

```text
ado doctor --profile work --json
ado auth check --profile work --non-interactive --json
ado project list --profile work --limit 100 --read-only
ado project get --profile work --project "Example Project"
ado project search --profile work --name backend --limit 500 --json
ado project list --organization example-org --token-stdin --auth-type pat --json --non-interactive --read-only
```

Supply stdin through your secret manager or CI secret input facility. Do not paste a
real token into a command/history. For bearer authentication use `--auth-type entra-token`.
`--token-prompt` is interactive only. Configure native providers as described in
[authentication](authentication.md).

`auth check` sends project get when a project is configured, otherwise projects list
with a one-item limit. It reports access to that endpoint, not all Azure DevOps services.
A public-resource response cannot independently prove credential validity; the JSON
explicitly states `credentialValidity: "not_independently_verified"`.
`doctor` checks local configuration and reports platform/provider information without
retrieving credentials or making a network request. It does not certify keyring access.

Project search is a case-insensitive client-side substring match on project names.
Its limit bounds projects scanned, not matches found. It does not invoke an undocumented
search API. Results include project ID, name, description, state and visibility;
unneeded remote properties/URLs are not returned.

## Pagination and bounds

`--top` controls requested page size (default 100); `--limit` controls scanned items
(default 100). `--all` scans up to profile pagination.maxItems (default 10,000) and
cannot be combined with an explicit --limit. `--continuation-token` accepts the Core
API's numeric project offset. No tokens are invented or used for other endpoint families.

Additional hard bounds: 100 pages, 4 MiB decompressed JSON per response, and a 64 MiB
aggregate-response threshold checked between pages. Request timeout defaults to 60
seconds, operation timeout to 300 seconds. Configured timeouts cannot exceed one day.
No more than three attempts per read, with jitter and Retry-After within the request
deadline. Redirects are refused and no authentication is forwarded.

JSON meta contains schemaVersion, organization, project, requestId, continuationToken,
truncated, completeness, truncationReason and scannedCount. With --require-complete,
truncation produces exit 10 and an error envelope retaining partial data. Otherwise
a bounded list returns 0 with explicit truncation metadata. Listing is not a snapshot
transaction; concurrent Azure DevOps changes can affect results across pages.

## Authentication precedence

Explicit --token/--token-stdin/--token-prompt/--credential-provider are mutually exclusive.
An explicit native provider requires its own complete --credential-service and
--credential-account; fields are not borrowed from a profile.

Without an explicit source: ADO_CREDENTIAL_PROVIDER (with ADO_CREDENTIAL_SERVICE and
ADO_CREDENTIAL_ACCOUNT) takes precedence over ADO_TOKEN, then the selected profile.
Auth type: --auth-type > ADO_AUTH_TYPE > profile. All inputs remain opaque. A failed
selected provider does not fall back. Explicit source overrides allow deliberate
organization changes; an inherited native reference cannot follow an organization override.

## Automation baseline

```text
ado project list --organization example-org --project "Example Project" --output json --non-interactive --read-only --limit 100 --timeout 30
```

Inject a least-privilege token without command-line arguments. Pin the CLI version,
set context/limits/timeouts explicitly, and inspect process exit, ok, error.code and
meta.truncated. Treat all project names/descriptions and future logs/package metadata
as untrusted. Never execute returned text as commands. Human project tables escape
control characters. JSON encodes strings, with no incidental banners or progress.

## Exit contract

| Exit | Meaning |
|---|---|
| 0 | Successful local inspection or bounded remote read |
| 1 | Unexpected internal failure (safe message only) |
| 2 | Syntax, input or credential-selection error |
| 3 | Configuration error |
| 4 | Credential missing/denied or HTTP authentication rejection |
| 5 | HTTP permission denial |
| 6 | Resource absent or inaccessible |
| 7 | Unsafe destination, redirect, conflict or safety refusal |
| 8 | Reserved for uncertain writes; no write commands yet |
| 9 | Timeout, exhausted transient failure or malformed service response |
| 10 | Response safety bound or strict completeness failure |
| 130 | User cancellation |

Errors never include raw server bodies, token values or native exception messages.
Warnings go to stderr. `--version` always prints only the semantic version.

Endpoint/scope sources: [Projects List](https://learn.microsoft.com/en-us/rest/api/azure/devops/core/projects/list?view=azure-devops-rest-7.1)
and [Projects Get](https://learn.microsoft.com/en-us/rest/api/azure/devops/core/projects/get?view=azure-devops-rest-7.1).
Both document vso.project and vso.profile. Use the applicable least-privilege credential
and Azure DevOps resource permissions; these OAuth scope identifiers are not Entra app roles.
