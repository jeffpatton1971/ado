# Verification and CI

Local NuGet manifest smoke test (no token/configuration required):

```powershell
dotnet run --project src/Ado.Cli --configuration Release -- package inspect --file "$env:TEMP\ado-Json.Input.Provider-1.1.0.nupkg" --output table --read-only
```

Expect Json.Input.Provider 1.1.0, a net9.0 dependency group, declarations for
Microsoft.Extensions.DependencyInjection (9.0.9) and Rackspace.BAT.Core.Abstractions
(2.8.0), and hashes matching the earlier local archive/member inspection.
Synthetic coverage includes range declarations, empty groups, no config access,
DTD rejection, duplicate manifests/identity and truncated-text rejection.

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

All service responses in this workflow are mocked. User-run live table diagnosis
and separate targeted log reads for build 18722 succeeded. Live JSON/non-interactive
and actual previous-attempt traversal remain outstanding; the same-build table
smoke tests do not establish those paths. M1.1 implementation/automated acceptance
is complete, not a claim of full platform or live-service release readiness.

## Test execution

`dotnet test Ado.slnx` runs unit, CLI and mocked-HTTP integration tests without live
Azure DevOps credentials or network calls. Package restore/audit reaches NuGet.
Windows tests additionally write/read/delete a uniquely named synthetic generic
credential with session persistence; this test skips on other platforms or unavailable
Windows logon sessions. Existing credential items are never enumerated or read.

The GitHub workflow builds/tests on Windows, Linux and macOS using SDK 10.0.400
selected from the exact `global.json` pin. It checks formatting,
audits direct/transitive dependencies, verifies --version against Directory.Build.props,
and creates an ephemeral tool package. Separate jobs cross-compile self-contained
outputs for x64 and Arm64 on all three OS families. Cross-compilation is not execution
coverage. There is no upload, release, NuGet push or deployment step.

CI action revisions are pinned to official actions/checkout v7 and actions/setup-dotnet
v6 commit IDs. They use MIT-licensed GitHub-maintained build-time tooling, not CLI
runtime dependencies. Update pins deliberately after reviewing upstream changes.

Local installation smoke checks use `dotnet tool install PattonTech.Ado.Cli
--tool-path .tools/ado-smoke --add-source artifacts/packages --version 0.1.0` after
packing. This directory is ignored and does not alter global PATH or publish the package.

This workflow has been authored locally but not run on GitHub because pushing has not
been authorized. Current local verification is Windows x64. macOS/Linux native keyring
validation and Arm64 runtime validation remain outstanding. The user reported successful
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
the signed PipelineArtifact path for that output; live Container download remains unverified.
The download symlink test skipped in the current Windows session because creating a
synthetic symlink requires unavailable privileges; cross-platform execution remains pending.

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
resume. Live JSON continuation, version listing and exact-version retrieval remain
pending. See [syntax](packages.md).

Package resolution mocks verify exact name/version selection, rejection of similarly
named packages and different versions, ambiguous identity failures, independent
package/version bounds (exit 10), and invalid selectors before authentication.
On 2026-09-18 the user verified organization-scoped resolution in rseng's automation
feed for `Json.Input.Provider` version `1.1.0`, with limit 100. The table returned
package ID `2894f7c9-8ec5-4679-a5d8-b5836dc971ee` and version ID
`945356c8-1454-4477-b38e-e9f37aed193c`. This exercises package and version metadata
reads through the resolver; standalone version commands and live JSON remain
unverified. No package was downloaded; downloadability and compatibility are not proven.

NuGet download mocks cover metadata resolution followed by exact content routing,
direct and redirected ZIP bytes/hash, credential isolation, unsafe-host rejection,
byte ceilings, non-ZIP content, 403, temporary cleanup, dry-run without credentials
and existing destination preservation. Build artifact tests also exercise the shared
bounded transfer implementation. Live NuGet download remains pending; tests do not
claim nuspec/signature verification or package compatibility.
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
