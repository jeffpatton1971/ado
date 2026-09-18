# Verification and CI

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

Live read-only tests require an explicitly authorized organization/project. Remote
mutation tests require separate approval of exact disposable target, effect/cost,
cleanup and reconciliation plan. No live test is automatically invoked by CI.
