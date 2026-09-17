# Verification and CI

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
validation, Arm64 runtime validation, and live API smoke tests remain outstanding.

Native macOS calls use a deprecated generic-password API and cannot safely abort an
active OS approval dialog. This is documented rather than claiming universal cancellation
or release readiness. Linux passes a cancellable to libsecret and suppresses unlock in
non-interactive mode.

Live read-only tests require an explicitly authorized organization/project. Remote
mutation tests require separate approval of exact disposable target, effect/cost,
cleanup and reconciliation plan. No live test is automatically invoked by CI.
