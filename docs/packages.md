# Feed package discovery and download

`package inspect --file <local.nupkg>` reports local package identity, dependency
groups and raw version/include/exclude declarations with archive/member SHA-256
values from one snapshot. No configuration, credentials or network are used.
`--expected-sha256` optionally checks the archive. Exactly one root nuspec is
required, using UTF-8 and at most 1 MiB / 10,000 lines. Shared archive safety
checks apply. DTDs, duplicate identities and mixed grouped/ungrouped dependencies
are rejected. Unsupported namespaces fail explicitly. This is not full schema
validation, signature verification or dependency/compatibility resolution.
See the [nuspec reference](https://learn.microsoft.com/en-us/nuget/reference/nuspec).

These commands inspect Azure Artifacts metadata without downloading packages:

- `ado package list --feed NAME_OR_ID` returns package GUID, name, normalized name
  and protocol. `--protocol NuGet` filters protocol; `--name TEXT` is a server-side
  substring search, not an exact dependency resolver. Embedded versions are omitted.
- `ado package versions --feed NAME_OR_ID --package-id GUID` returns non-deleted
  version metadata, including version GUID, display/normalized version, optional
  listed/deleted/latest flags and publish date. Missing flags remain null/unknown.
- `ado package version get --feed NAME_OR_ID --package-id GUID --version-id GUID`
  retrieves an exact version identity, verifying its returned GUID. This does not
  accept a version string/range, normalize a dependency requirement or select latest.
  Deleted metadata can be returned by this direct read and is not called downloadable.
- `ado package resolve --feed NAME_OR_ID --name EXACT_NAME --package-version LITERAL`
  resolves one NuGet name/version to package and version GUIDs. Names match display
  or normalized names case-insensitively; versions match a reported display or
  normalized string using exact case-sensitive text. No version-range evaluation,
  inferred normalization, latest fallback or dependency-graph traversal is performed.
  Use the service's version spelling (for example, `1.0.0`, not an assumed `1.0`).
  Both scans must be complete within bounds; otherwise exit 10 reports an incomplete
  search without claiming a resolution, even when a candidate was encountered.
  Absent exact matches in complete visible inventories return exit 6 with a
  scope/visibility-qualified message. Ambiguous exact matches fail as invalid service
  responses. `--limit` applies separately to package candidates and version inventory;
  `--all` uses the configured maximum. Resolve starts at offset zero, accepts no
  continuation, and shares one operation deadline across its reads. Its JSON
  scanned count sums package candidates and version rows. These separate reads are
  not a transactional snapshot. Visibility defaults from the underlying lists apply.

`ado package download` accepts the same exact NuGet `--name` and `--package-version`
selectors, plus a required new-file `--destination`. It resolves complete bounded
metadata first, then requests content using the resolved package name and normalized
version (display version if normalization is absent). `--scope`, `--feed`, `--limit`,
`--all` and `--top` retain their resolution meanings. This is allowed with `--read-only`:
remote requests are reads; the local destination is explicitly requested.

```powershell
dotnet run --project src/Ado.Cli --configuration Release -- package download --scope organization --feed automation --name Json.Input.Provider --package-version 1.1.0 --destination "$env:TEMP\ado-Json.Input.Provider-1.1.0.nupkg" --config ./config.json --token-prompt --output table --read-only --limit 100
```

The documented content endpoint uses `pkgs.dev.azure.com` API `7.1-preview.1`.
Only its initial scoped request receives the credential; up to five redirects use
the existing allowed HTTPS artifact-storage hosts through an isolated client, without
credentials/cookies. No automatic content retry occurs. Download byte/time settings
apply; `--max-bytes` and `--download-timeout` can lower configured ceilings. The shared
operation deadline includes resolution. Output is written to a new temporary sibling,
then published without overwriting after bounded transfer and ZIP-envelope checks.
Failure cleans up the operation's temporary output. Existing destination and unsafe
parent-path rules are shared with build artifact download.

Output includes resolved package/version identities, downloaded bytes and SHA-256.
It labels content verification `zip_envelope_only`: no nuspec identity, signatures,
archive members or dependency semantics have been verified. Metadata and content
reads are separate, not a snapshot; the digest is not proof of publisher authenticity.
No downloaded code executes. Use existing local `artifact inspect` for bounded ZIP
inventory/member reads. `--dry-run` validates inputs/destination and prints a local
plan without credential lookup, network access, resolution or file writes.

Use `--scope organization` for the user's automation feed. The default project scope
uses the configured project. Scope, feed and IDs are explicit; returned links are
never followed. URLs, descriptions, authors, files and upstream payloads are omitted.

```powershell
dotnet run --project src/Ado.Cli --configuration Release -- package list --scope organization --feed automation --protocol NuGet --config ./config.json --token-prompt --output table --read-only --limit 20
```

Package listing sends `includeAllVersions=false`, `includeDeleted=false`,
`includeUrls=false` and `includeDescription=false`. Other service visibility/version
defaults apply; the command does not inventory every package state or all versions.
Pagination uses documented `$top`/`$skip`: `--top` controls page size, `--limit`
bounds rows and `--all` uses the configured ceiling. JSON continuation is a numeric
offset generated by the CLI. Resume with `--continuation-token` and the same scope,
feed and filters. Short nonempty pages do not establish exhaustion; an empty page
does. Reaching a bound reports partial results even if the final page happened to
contain the last package. Duplicate IDs, oversized pages and unexpected continuation
headers fail. Limits include 100 pages, 64 MiB aggregate and 4 MiB per response.
Offset paging is not a transactional snapshot: concurrent changes may shift results.

Version listing has no documented server pagination: `--limit`/`--all` bound local
output within a 4 MiB response and 10,000-version ceiling. It requests `isDeleted=false`
and `includeUrls=false`; other service defaults remain. Truncation has no cursor;
raise the bounded limit. `--top` and `--continuation-token` are rejected here.
`--require-complete` on either list preserves partial JSON and returns exit 10.

Completeness describes returned metadata in the selected scope/filter, not global
availability. An empty search does not prove absence; HTTP 401/403/404 retain their
error categories, including masked-not-found ambiguity. These calls do not establish
download permission, compatibility or package contents. Version-range/dependency-graph
solving is outside this command. Exact NuGet download and structured local nuspec
inspection are implemented and user-verified for Json.Input.Provider 1.1.0.
The user verified NuGet package listing in the organization-scoped automation feed
with 20 returned rows and a bounded-search warning. Live continuation remains pending.
Standalone version listing returned eight visible Json.Input.Provider versions with
`--require-complete` and no truncation warning; exact GUID retrieval returned 1.1.0.
The user also verified `package resolve` for Json.Input.Provider 1.1.0, returning
package ID `2894f7c9-8ec5-4679-a5d8-b5836dc971ee` and version ID
`945356c8-1454-4477-b38e-e9f37aed193c`. This verifies metadata resolution only;
live JSON remains unverified. Standalone version commands are now user-verified.
The user verified destination-exists refusal before a token prompt, then externally
expanded an existing Json.Input.Provider 1.1.0 nupkg. Its download origin and digest
were not established by that output. A subsequent CLI download succeeded with
18,092 bytes and SHA-256
`d229edb039c1555af0f136f67d4d1d12a526f56e1820be943598c091fd45b2d9`.
Subsequent local artifact inspection verified the nuspec text and archive/member
hashes (see testing.md). The manifest declares Json.Input.Provider 1.1.0 and a net9.0
dependency group. Subsequent `package inspect` output verified structured identity
and dependency declarations with matching archive/member hashes. These checks do
not establish dependency resolution, compatibility or publisher authenticity.

References: [packages](https://learn.microsoft.com/en-us/rest/api/azure/devops/artifacts/artifact-details/get-packages?view=azure-devops-rest-7.1),
[versions](https://learn.microsoft.com/en-us/rest/api/azure/devops/artifacts/artifact-details/get-package-versions?view=azure-devops-rest-7.1),
[exact version](https://learn.microsoft.com/en-us/rest/api/azure/devops/artifacts/artifact-details/get-package-version?view=azure-devops-rest-7.1).
Content: [NuGet download](https://learn.microsoft.com/en-us/rest/api/azure/devops/artifactspackagetypes/nuget/download-package?view=azure-devops-rest-7.1).
