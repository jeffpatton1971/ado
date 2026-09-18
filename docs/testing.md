# Verification and CI

Exact-source acceptance includes a two-page fixture with identical pipeline/build
names, successful builds of other commits and failed/successful runs of the selected
commit. Only the exact SHA matches survive, retaining distinct build IDs and results.
The bounded variant returns partial data, scan count and continuation with exit 10.
This does not infer that separate build IDs are retry attempts: intra-build retries
are covered by timeline-history and diagnostic-workflow reference tests. Actual
live retry fixtures are unavailable: the user could not identify an existing
retried job/stage run. This is a documented live-coverage limitation, not an
independent M1 blocker; no work pipeline will be rerun to create a fixture.

Repository-filter acceptance: ordinary build searches now reject wrong or missing
returned repository IDs/types, including a successful build with the requested SHA.
Four CLI regression cases exercise this through the JSON error contract. Existing
source-search continuation coverage now supplies matching repository identity.

Package automation acceptance: the direct and storage-redirect download cases now
consume the JSON destination/digest in `package inspect`, then use its manifest
path/hash in `artifact evidence`. Checks verify identity and digest agreement,
schema version, no raw member content in evidence, no invented remote provenance,
and no publication on expected-hash mismatch. Local steps run non-interactively
without credentials, with a nonexistent config and a handler that rejects HTTP.
This is synthetic end-to-end coverage, not additional live service verification.

For a live JSON/non-interactive diagnosis check in PowerShell 7, supply the token
through stdin; the shell prompts, but ado itself must not prompt:

```powershell
Read-Host "Token" -MaskInput | dotnet run --project src/Ado.Cli --configuration Release -- build diagnose --config ./config.json --build-id 18722 --token-stdin --non-interactive --json --read-only --include-history --limit 100 --require-complete
```

Expect one JSON envelope with `ok:true`, schemaVersion 1 and complete metadata.
The failed build is diagnostic data, not a CLI failure. This known run has no
previous-attempt references, so it cannot verify live retry-history traversal.

`AuthProbeTests` verifies selected build, artifact-list and organization-feed GET
routes, no fallback project request, 401/403/404 failure propagation and required
context rejection before credential retrieval. Live commands are documented in
[authentication](authentication.md). The user verified the organization-scoped
automation feed probe: accessConfirmed:true, authenticationType:pat and
checkedCapability:"feed get", with credentialValidity:not_independently_verified.
The user also verified build get for build 18722 and build artifact list for build
18522, both with accessConfirmed:true and PAT authentication. These three probes
were run in read-only/table mode with token prompts. Live JSON/non-interactive
automation remains pending; these checks do not establish broader permissions.

Local NuGet manifest smoke test (no token/configuration required):

```powershell
dotnet run --project src/Ado.Cli --configuration Release -- package inspect --file "$env:TEMP\ado-Json.Input.Provider-1.1.0.nupkg" --output table --read-only
```

Expect Json.Input.Provider 1.1.0, a net9.0 dependency group, declarations for
Microsoft.Extensions.DependencyInjection (9.0.9) and Rackspace.BAT.Core.Abstractions
(2.8.0), and hashes matching the earlier local archive/member inspection.
Synthetic coverage includes range declarations, empty groups, no config access,
DTD rejection, duplicate manifests/identity and truncated-text rejection.

User-reported success: structured inspection returned Json.Input.Provider 1.1.0,
18,092 archive bytes, the net9.0 group and both expected dependency declarations
with `exclude="Build,Analyzers"`. Archive SHA-256:
`d229edb039c1555af0f136f67d4d1d12a526f56e1820be943598c091fd45b2d9`;
manifest SHA-256:
`42124df9879ecfe80c705cce0fc198862900e78cb3ea64b5788d494c396b7366`.
Both match the prior local text inspection. Subsequently, the user verified CLI
download from organization-scoped automation for the same package/version:
18,092 bytes with the identical archive SHA-256. Exact package/version GUIDs matched
the earlier resolution. External PowerShell extraction succeeded and the user
removed the extracted directory. This verifies live content retrieval and digest
agreement, not publisher authenticity, signatures or dependency compatibility.

## Read-only diagnostic workflow acceptance

DiagnosticWorkflowTests exercises the actual CLI entry point with a supported run
URL, synthetic --token-stdin credentials, --json, --non-interactive and --read-only.
It performs build diagnosis with current and previous-attempt timelines, selects
the non-historical failed task's log ID from the JSON findings, then requests that
log. Tests assert canonical GET routes, no implicit log reads during diagnosis,
one JSON envelope per invocation, no prompts and selected-credential redaction.
Range and local-line limits retain data with strict exit 10; absent/forbidden logs
and unauthorized/forbidden timeline reads return their error exit codes without
echoing service bodies. Existing diagnosis tests cover truncated timeline evidence.

All service responses in the automated tests are mocked. User-run live table
diagnosis and separate targeted log reads for build 18722 succeeded. The user also
verified the stdin-token, JSON/non-interactive/read-only diagnosis command above
with --include-history and --require-complete. It returned ok:true, schemaVersion:1,
loadedRecords/scannedCount:30, truncated:false and completeness:complete. Nine
findings comprise one failed task (log 21), three failed containers and five skips.
The reported source SHA matches the earlier table result. All findings have attempt
1 and no previous-attempt references; this does not verify history traversal.
The user then retrieved build 18722/log 21 using stdin credentials, JSON,
non-interactive/read-only mode, limit 100 and --require-complete. The response
reported ok:true, schemaVersion:1, scannedCount:19, truncated:false and
completeness:complete. It exposed the previously observed missing golden JSON
file error. Raw log contents are not retained here. This verifies the user-driven
diagnosis-to-log sequence by build ID; live URL-based automation and actual retry
traversal remain unverified. The chat-rendered paste is not a byte-for-byte JSON
capture, so this records reported envelope fields rather than a parser assertion.
M1.1 automated acceptance is
complete, not a claim of full platform or live-service release readiness.

## Test execution

`dotnet test Ado.slnx` runs unit, CLI and mocked-HTTP integration tests without live
Azure DevOps credentials or network calls. Package restore/audit reaches NuGet.
Windows tests additionally write/read/delete a uniquely named synthetic generic
credential with session persistence; this test skips on other platforms or unavailable
Windows logon sessions. Existing credential items are never enumerated or read.

The GitHub workflow builds/tests on Windows, Linux and macOS using SDK 10.0.400
selected from the exact `global.json` pin. It checks formatting,
audits direct/transitive dependencies, verifies --version against Directory.Build.props,
and creates a tool package, then installs and executes it through an isolated
tool path on each test OS. Separate jobs cross-compile self-contained
outputs for x64 and Arm64 on all three OS families. Cross-compilation is not execution
coverage. There is no upload, release, NuGet push or deployment step.

CI action revisions are pinned to official actions/checkout v7 and actions/setup-dotnet
v6 commit IDs. They use MIT-licensed GitHub-maintained build-time tooling, not CLI
runtime dependencies. Update pins deliberately after reviewing upstream changes.

Run the packaged-command smoke check with PowerShell 7:

```powershell
dotnet pack src/Ado.Cli --configuration Release --no-build --output artifacts/packages
./scripts/Test-ToolPackage.ps1
```

Build Release first. The script reads the version from Directory.Build.props and
installs only from the local package source, using an isolated NuGet cache and
tool path. It executes the installed command's version/help, synthetic nuspec
inspection twice (identical JSON), and expected-hash rejection with exit 7.
Outputs remain under ignored artifacts/tool-smoke for investigation. The prior
NUGET_PACKAGES process value is restored. No global PATH change, publication,
Azure DevOps request, credential access or real config read is involved.
The installed 0.1.0 command passed locally on Windows x64 and in hosted CI on
Windows x64, Linux x64 and macOS Arm64. The first CI run exposed publish-lock configuration
and macOS symlinked temporary-path failures. Publish restores now generate separate
obj/publish-packages.lock.json files; normal test restores still use checked-in
locks in locked mode. macOS tests use the canonical runner temporary path, keeping
the production rejection of symlink ancestors intact.

The branch was pushed with user authorization. [CI run 35360735378](https://github.com/jeffpatton1971/ado/actions/runs/35360735378)
passed all nine jobs at commit 32ea5c8: three OS test/format/audit/package/install
jobs and six self-contained cross-compiles. Windows passed 563 tests with no skips;
Linux/macOS each passed 562 and skipped only the Windows native credential test.
This also verifies disposable Windows native credential storage on the hosted runner.
The user subsequently installed ado on macOS following the README and reported
successful auth check with macos-keychain, including non-interactive and repeated
invocations. This verifies the native provider's successful access path on that
installation; the user did not specify its architecture. Locked/denied/missing-item
paths and executable relocation/re-authorization are not established by this report.
Linux native keyring validation and Windows/Linux Arm64 runtime validation
remain outstanding; cross-compilation alone does not close those gaps. The user reported successful
live Core project listing/retrieval and pipeline listing/run-history checks on 2026-09-17.
Live individual pipeline/run retrieval and submission remain unverified; the development
agent has not accessed the live service. Run submission tests use synthetic credentials
and mocked HTTP, including no-dispatch safety checks and uncertain-write outcomes.
The user verified local run-start dry-run for pipeline 1128. Server YAML preview tests
also use mocked HTTP: forced previewRun:true, exact preview confirmation, read-only
blocking, no retry, bounded responses and opt-in YAML redaction. The user reported live
server-preview success for pipeline 1128 (13,778 YAML characters, content omitted).
No live server preview has been performed by the development agent. Build list/get
have mocked route/filter/pagination/output coverage and user-reported live success for
definition 1128/build 18722. Log index/content tests use mocked HTTP and cover response
and output bounds, line ranges, redaction, terminal escaping and completeness. The user
reported a successful 26-log index and content retrieval for log 3 of build 18722.
Build-output metadata list/get also have user-reported live success for build 18522,
including CompiledOutputs (17112). ZIP download tests use generated in-memory archives
and mocked HTTP only, covering redirect credential isolation, bounds, interruption,
cleanup, file conflicts and local dry-run. User-run downloads reached the artifact
service but were blocked on its sign-in redirect. PipelineArtifact downloads now
request signedContent; tests cover credential isolation, expiry and identity checks.
On 2026-09-17 the user successfully downloaded CompiledOutputs from build 18522
(4,455,202 bytes), then extracted and listed its contents with PowerShell. This verifies
the signed PipelineArtifact path for that output; Container validation is recorded below.
The user rechecked build 18522: CompiledOutputs (17112) and NuGetPackages (17115)
both report PipelineArtifact. Neither is a classic Container live-test fixture.
The user subsequently found a classic Container fixture with build artifact list:
build 7826, artifact ID 5163, name drop, resourceType Container. The subsequent
read-only download succeeded: 654-byte ZIP, inspected locally with two entries
(drop/ and drop/20210113.1.json). The file member reports 1,960 expanded bytes and
414 compressed bytes. Archive SHA-256:
`3d692ddfe117f08bf9bdf3517bc4055509b977aafbcd82bf3ac07d33e8c3e830`.
PowerShell extraction succeeded; the user listed the selected file and removed
the extracted directory. A subsequent exact-member inspection hashed
`drop/20210113.1.json` as
`8c39e99a0b7af428c10dcf152970fb74bbea1821892234298ef0a22388a6f488`.
The user then successfully exported service-backed evidence for the same build,
artifact and member. Both archive and member digests matched the local inspection;
the export reported `authenticated_metadata_and_download` and
`complete_for_selected_member`. This completes live positive-path coverage for
both supported artifact types. It does not establish publisher authenticity or
whole-package verification. Raw member contents have not been retained in the
repository.
The download symlink test skipped in the current Windows session because creating a
synthetic symlink requires unavailable privileges; the Linux/macOS CI suites passed
this coverage (their only skipped test was the Windows native credential test).

Native macOS calls use a deprecated generic-password API and cannot safely abort an
active OS approval dialog. This is documented rather than claiming universal cancellation
or release readiness. Linux passes a cancellable to libsecret and suppresses unlock in
non-interactive mode.

Build repository identity tests cover list/get projection, string IDs (including
GitHub owner/repository identifiers), missing identities, malformed field types,
credential redaction, omission of URLs/properties and JSON/table terminal escaping.
On 2026-09-18 the user verified build get table output for build 18722 in
rseng/impldevmpc: definition 1128, completed/failed, source version
`a357ab0a6900e27bcaa318497ed11ff845fcfa16`, repository ID
`global-build/rackspace-bat-api`, type `GitHub`, and name displayed as `unknown`.
The source version matches the previously observed build and pipeline run resource.
This verifies reported identity rendering, not repository contents or current HEAD.
Live list/JSON identity output and embedded evidence remain unverified.

Run repository provenance tests cover multiple reported revisions, missing and empty
resources, missing versions, duplicate aliases, malformed values, credential redaction,
omission of sensitive payload fields, JSON/table output and terminal escaping.
The single mocked run GET is the only request; no current repository/definition read
is used to manufacture historical provenance.
On 2026-09-18 the user verified `pipeline run get` for pipeline 1128, run 18722
in rseng/impldevmpc. The completed/failed run reported `reported_versions` with:

- `self` (gitHub), ref `refs/heads/feature/mpcsupeng-9171-json-runtime`, version
  `a357ab0a6900e27bcaa318497ed11ff845fcfa16`, matching the previously reported build source.
- `buildAutomationTool` (gitHub), ref `refs/heads/main`, version
  `3a968467954c6f99bf6d6b18e106914702fc92be`.

This verifies table rendering of service-reported repository resources; it does not
independently verify commit contents, all checkouts or shared-template usage.
Live JSON output, missing-resource states and other repository types remain unverified.

PR context tests cover reason-plus-ref inference, reason-only and ref-only evidence,
canonical positive PR numbers, malformed/overflow refs, unsupported repository types,
missing revisions and an explicitly unresolved head version. List JSON and get
JSON/table tests verify integration. On 2026-09-18 the user verified build get table
context for build 18856 (definition 1124, completed/succeeded) in rseng/impldevmpc.
The repository was `global-build/rackspace-output-terraform`, type `GitHub`, name
unavailable. Source ref `refs/pull/8/merge` and version
`55d3be0388e923740b230bf6971e42ebc022eaf0` produced PR number 8, evidence
`pull_request_reason_and_merge_ref`, built version kind `reported_merge_commit`,
and head version `unknown (not_resolved)`. This verifies service-reported context
rendering, not independent merge verification or historical PR head resolution.
Live JSON context and alternative evidence states remain unverified.

PR discovery mocks cover GitHub/TfsGit repository-scoped merge refs, pullRequest
reason filtering, continuation preservation, rejection of wrong/missing repository
identity, wrong refs and non-PR reasons, and validation before authentication.
A PR head SHA cannot match a different built merge SHA.
On 2026-09-18 the user verified PR discovery in rseng/impldevmpc for GitHub repository
`global-build/rackspace-output-terraform`, PR 8, definition 1124, with limit 100.
The table returned build 18856 (20260917.1), completed/succeeded, on `refs/pull/8/merge`,
with no truncation warning. The user reported that `global-build/rackspace-bat-api`
has no PR-triggered builds, so it was not used for this smoke test.
Live JSON metadata, PR pagination, TfsGit and exact merge-SHA filtering remain
unverified; this does not establish the PR head SHA or independently verify a merge.

Build discovery tests cover repository query encoding, full-SHA validation before
credential lookup, exact case-insensitive source matching, nonmatching scan limits,
continuation resumption, retained partial JSON and missing-source completeness.
On 2026-09-18 the user verified a live exact-SHA search in rseng/impldevmpc for
definition 1126 and source c965c728a906498c4ce8439fcf4092a388a4c630 with limit 100.
The command reported 71 scanned builds, one match and complete coverage of the
filtered service listing. The match was build 18522 (20260825.4), completed/succeeded,
on refs/heads/feature/mpcsupeng-9381-escape-terraform-template-interpolation.
Repository filters, partial-search resumption and missing-source handling remain
mock-tested only; this does not verify PR or shared-template provenance.

Feed list/get mocks verify project and organization routes, allowlisted identity,
project association, validation before credentials, partial JSON with exit 10,
HTTP 401/403/404 errors, mismatched feed/project rejection, credential redaction
and terminal escaping. On 2026-09-18 the user verified project-scoped feed list
in rseng/impldevmpc with limit 100: `testing-upstream`, ID
`ad52ea46-c29f-4c9c-9b36-600181eb40da`, scope project, project impldevmpc.
The user also verified `feed get --scope organization --feed automation` in rseng:
ID `09093787-fb1b-4624-be02-8b4a92580114`, name automation, organization scope,
and no project association. Organization listing, project-scoped get and live JSON
remain pending. See [feed syntax](feeds.md).

Package tests cover scoped routes, encoded protocol/name filters, numeric offset
resumption, short pages, repeated-page rejection, non-deleted version listing,
local truncation, exact version-GUID validation, omitted sensitive payloads,
redaction, table escaping and 401/403/404 errors. Invalid selectors fail before
credential acquisition. On 2026-09-18 the user verified organization-scoped package
listing in rseng's automation feed, protocol NuGet, limit 20. The table returned 20
package identities and the bounded-search warning. One returned package was
`Json.Input.Provider`, ID `2894f7c9-8ec5-4679-a5d8-b5836dc971ee`.
This verifies listing and table warning behavior, not exhaustion or a successful
resume. The user subsequently verified live JSON/non-interactive listing with
stdin credentials and limit 2: aws/azr returned continuationToken 2; resuming
with the same scope/feed/protocol returned build/coverlet.collector and token 4.
Both envelopes reported schemaVersion 1, project null, truncated true,
completeness partial, truncationReason item_limit and scannedCount 2.
The two pages had distinct package IDs. This verifies numeric-offset resumption,
not exhaustion or snapshot consistency. See [syntax](packages.md).
The user subsequently verified standalone version listing for this package with
limit 100 and --require-complete: eight visible versions, no truncation warning.
Exact version retrieval returned GUID `945356c8-1454-4477-b38e-e9f37aed193c`,
version 1.1.0, listed/latest true and deleted unknown. The output does not establish
unrestricted visibility or interpret a missing deleted flag as false.

The user verified organization-scoped NuGet name search for Yaml, returning
Yaml.Input.Provider and YamlDotNet. Version inventory for Yaml.Input.Provider
(`5b2d651a-0ec3-4533-830e-c45f7bbe72d2`) with --all --require-complete returned
without a truncation warning. Version 2.5.0 has ID
`f350c717-cd33-48b2-aa43-67216d8e8fc8`, listed/latest true and deleted unknown.
This establishes visible metadata, not the version required by a downstream
project; exact YAML resolution and content retrieval are not claimed by this check.

Package resolution mocks verify exact name/version selection, rejection of similarly
named packages and different versions, ambiguous identity failures, independent
package/version bounds (exit 10), and invalid selectors before authentication.
On 2026-09-18 the user verified organization-scoped resolution in rseng's automation
feed for `Json.Input.Provider` version `1.1.0`, with limit 100. The table returned
package ID `2894f7c9-8ec5-4679-a5d8-b5836dc971ee` and version ID
`945356c8-1454-4477-b38e-e9f37aed193c`. This exercises package and version metadata
reads through the resolver; standalone version commands were subsequently verified
as recorded above. Live JSON remains unverified. Resolution itself downloads no
package and does not prove downloadability or compatibility.

The user also verified Rackspace.BAT.Core.Abstractions 2.8.0 in the organization-scoped
automation feed. With --limit 100, resolution returned version_search_incomplete
without asserting a match. Retrying with --all succeeded: package ID
`0602c6b1-d194-436f-9918-715e051390e3`, version ID
`5104d1e4-a3c3-40e8-b44d-f9da94bfd915`, version 2.8.0. This verifies live bounded
refusal followed by exact metadata resolution within configured ceilings, not
package download, compatibility or dependency closure.

NuGetInspectionTests also constructs a package containing plugin.json and a binary
DLL entry with invalid UTF-8. Exact selection through the JSON/non-interactive CLI
verifies member lengths and hashes, complete metadata and omitted text, with no
configuration or HTTP access. This tests byte inspection, not assembly loading or
compatibility. No executable package content is required by the fixture.

NuGet download mocks cover metadata resolution followed by exact content routing,
direct and redirected ZIP bytes/hash, credential isolation, unsafe-host rejection,
byte ceilings, non-ZIP content, 403, temporary cleanup, dry-run without credentials
and existing destination preservation. Build artifact tests also exercise the shared
bounded transfer implementation. Live NuGet download and matching digest were
subsequently user-verified (see the local manifest section above); transfer checks
do not claim nuspec/signature verification or package compatibility.
On 2026-09-18 the user ran package download for Json.Input.Provider 1.1.0 against
an existing destination and received `destination_exists` before any token prompt.
The user then successfully expanded that existing nupkg with PowerShell and listed
its contents: a nuspec, plugin.json, documentation, lib/net9.0 DLL/XML and ZIP/NuGet
metadata entries. This verifies the live no-overwrite guard and external extraction
of an existing local file. The pasted output does not establish the file's download
origin or digest and is not evidence of a successful CLI content transfer.
The user subsequently verified local `artifact inspect --show-text` for
`Json.Input.Provider.nuspec`: archive 18,092 bytes, 9 entries, SHA-256
`d229edb039c1555af0f136f67d4d1d12a526f56e1820be943598c091fd45b2d9`;
member 841 bytes (457 compressed), SHA-256
`42124df9879ecfe80c705cce0fc198862900e78cb3ea64b5788d494c396b7366`.
The displayed manifest declares Json.Input.Provider 1.1.0, a net9.0 dependency
group, Microsoft.Extensions.DependencyInjection version text 9.0.9 and
Rackspace.BAT.Core.Abstractions version text 2.8.0. These are declarations, not
resolved dependency identities or proof of compatibility. The repository element
supplies only type git, without a URL or commit. This verifies local member text
and digests; remote download provenance, signatures and structured nuspec parsing
remain unverified by this output.

Live read-only tests require an explicitly authorized organization/project. Remote
mutation tests require separate approval of exact disposable target, effect/cost,
cleanup and reconciliation plan. No live test is automatically invoked by CI.
