# Optional live cross-platform integration

`Live read-only integration` is a manually dispatched workflow running the installed
NuGet tool on Windows, macOS and Linux. It accepts only the `main` ref and uses the
`ado-integration` GitHub environment. Ordinary push/PR CI remains credential-free.
This workflow is optional release evidence, not a required PR check. It publishes
no packages, uploads no outputs and performs no remote writes.

## Setup after merge

1. Create the repository environment **ado-integration**. Restrict its deployment
   branches to **main** and enable required reviewers where the repository plan
   supports them. Review the selected main commit before approving a run. The YAML
   branch condition alone is not a security boundary against changed workflow code;
   environment branch restrictions and protected/reviewed main are essential.
2. Add environment secret **ADO_PAT**: a dedicated, expiring, least-privilege PAT
   with Build (Read) and Packaging (Read), owned by an identity with access only to
   the intended fixtures where feasible. Do not use an everyday developer token.
3. Add environment variable **ADO_LIVE_FIXTURES** with the JSON shape below. These
   identifiers must be suitable for repository administrators to view. Use a test
   project with retained, non-sensitive builds/artifacts and small log/package fixtures.
4. In Actions, select **Live read-only integration → Run workflow → main**. Approve
   environment access if configured. No workflow runs automatically with this PAT.

```json
{
  "organization": "example-org",
  "project": "CLI Integration",
  "buildId": 34,
  "sourceSha": "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
  "logId": 21,
  "artifacts": [
    {
      "buildId": 35,
      "name": "CompiledOutputs",
      "resourceType": "PipelineArtifact",
      "entry": "CompiledOutputs/plugin.json",
      "memberSha256": "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"
    },
    {
      "buildId": 36,
      "name": "drop",
      "resourceType": "Container",
      "entry": "drop/manifest.json",
      "memberSha256": "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb"
    }
  ],
  "scope": "organization",
  "feed": "cli-integration",
  "packageName": "Example.Package",
  "packageVersion": "1.0.0",
  "packageSha256": "cccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccc"
}
```

Replace every illustrative ID, name and hash with your fixture values. The build
must have a failed task referencing logId; its complete timeline and selected log
must each fit the 1,000-item/line test bound. The feed needs at least two visible
NuGet packages for pagination. Both artifacts and the exact package must remain
available, within CLI transfer bounds, with the expected member/package digests.
Artifact ZIP bytes may change between downloads, so assertions use selected-member
hashes. Package verification additionally checks the downloaded nuspec identity.

Validate fixture syntax locally without reading a token or contacting Azure DevOps:

```powershell
$env:ADO_LIVE_FIXTURES = Get-Content ./live-fixtures.json -Raw
pwsh -NoProfile -File scripts/Test-LiveIntegration.ps1 -ValidateOnly
```

Keep private fixture files out of Git. Do not paste the token into fixture JSON.
The workflow constructs a temporary non-secret CLI configuration, injects the PAT
only for the live step, and runs fixed `--read-only --non-interactive --json`
commands. The harness consumes stdout/stderr internally and prints only fixed
pass/fail labels. Raw logs, metadata and downloads are not uploaded; operation-owned
temporary downloads are removed in finally. Hosted runner disposal covers job termination.

## Interpretation and limits

Missing configuration fails explicitly; it is not a skipped success. Failures may
indicate expired credentials, permission changes, fixture retention/deletion,
network/service issues or regressions. Investigate without posting raw private logs.
Do not replace failed fixtures or expected hashes merely to make a run pass.

The token source is environment injection. This does not verify native credential
stores, Entra sign-in, publisher authenticity, dependency closure or live retries.
No live workflow has been executed as part of adding this option. Synthetic harness
checks run in ordinary CI and require no real service token.
