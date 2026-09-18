# Classic release inspection

`release list`, `release get`, `release environments`, `release approvals` and
`release deployments` and `release tasks` inspect classic Azure DevOps
releases, separate from YAML pipeline runs and builds. All require an organization and project and use
the Release API 7.1 on vsrm.dev.azure.com. The documented read scope is vso.release;
successful Build API reads do not establish release access.

```powershell
dotnet run --project src/Ado.Cli --configuration Release -- release list --config ./config.json --token-prompt --output table --read-only --limit 20
```

Use `release get --release-id` with a positive ID returned by the list command.
List supports `--definition-id` for a classic release definition (distinct from a
Build definition). Results are requested newest-created first. Release status is
the release's lifecycle status, not an environment's deployment result.

JSON returns id, name, definitionId, definitionName, status, createdOn and modifiedOn.
Table output shows the two IDs, name and status. Variables, environment payloads,
approvals, identities, artifacts, descriptions, properties and URLs are omitted.
Known credentials are redacted and terminal control characters escaped.

List uses --top page sizes and --limit or --all within configured ceilings, at most
100 pages and a 64 MiB aggregate response budget, plus the transport's 4 MiB per-response
ceiling. Resume with the numeric JSON meta.continuationToken and the same definition
filter. Malformed or repeated tokens, duplicate releases and oversized pages fail.
Get may return a project reference with a GUID and null name. For a configured name,
the CLI accepts this documented form and relies on the project-scoped request route;
an explicit returned name must match. A configured GUID must match the returned GUID.
Truncated results retain their continuation token; --require-complete retains data
but returns exit 10. An empty complete list succeeds. Get validates the requested ID.

--read-only is supported. As with other metadata reads, --dry-run still reads the
service. No create, deploy, approve or cancel commands are implemented here.

Mock tests cover routing, pagination, redaction, project/definition/release identity,
invalid continuations and CLI output. The user verified live listing in impldevmpc:
20 releases were returned with a truncation warning, including release 1492 from
definition 20. The user also verified release get for 1492 (Release-58, definition 20,
status active) after the ID-only project reference fix. Continuation resume remains unverified.

## Environment status

```powershell
dotnet run --project src/Ado.Cli --configuration Release -- release environments --config ./config.json --release-id 1492 --token-prompt --output table --read-only --limit 100
```

This uses Get Release to return an environment summary: id, releaseId,
definitionEnvironmentId, name, status and rank. Results retain service order.
Environment IDs identify instances within a release; definition environment IDs
identify their templates. Status is the service's environment deployment status.
Variables, owners, approvals, deployment steps and logs are omitted.

The command makes one bounded GET. --limit and --all control the local item bound,
not response size; the 4 MiB transport ceiling still applies. There is no continuation
token. An empty array is complete; missing/null environment arrays fail. Truncation
is explicit, and --require-complete preserves results but returns exit 10. This
summarizes current environments, not deployment-attempt history. Mock coverage includes
identity checks, malformed responses, redaction, terminal escaping and completeness;
the user verified release 1492 returned environment 1499 (ib-tasks), definition
environment 22, status rejected and rank 1. This summary does not establish the
reason for the rejected status.

## Approval status

```powershell
dotnet run --project src/Ado.Cli --configuration Release -- release approvals --config ./config.json --release-id 1492 --token-prompt --output table --read-only --limit 100
```

This reads the release's preDeployApprovals and postDeployApprovals arrays with one
Get Release request. It returns approval ID, release/environment IDs, environment
name, phase, status, isAutomated, attempt and rank. It does not approve or reject
anything. Approver identities, comments, URLs, nested history and approval-definition
snapshots are omitted. It does not establish why an approval has its current status.

--limit/--all bound output across all environments and both phases; order follows
the service's environments, pre-deployment then post-deployment. The response remains
subject to the 4 MiB transport ceiling. There is no continuation token. Empty arrays
are complete; missing/null approval arrays mark completeness unknown. A missing
environments array is invalid. --require-complete retains data with exit 10 for
truncated or unknown results. This is a snapshot, not a complete approval audit trail.
Mock tests cover route, phases, limits, identity mismatches, malformed responses,
redaction, terminal escaping and strict completeness. The user verified release 1492
returned approval 3614 for environment 1499 (ib-tasks): preDeploy, approved,
isAutomated true, attempt 1, rank 1. This approval does not explain the environment's
rejected status.

## Deployment attempts

```powershell
dotnet run --project src/Ado.Cli --configuration Release -- release deployments --config ./config.json --release-id 1492 --token-prompt --output table --read-only --limit 100
```

This makes one Get Release request and summarizes each environment's deploySteps:
step ID, deployment ID, release/environment IDs, environment name, attempt, status,
operationStatus and hasStarted. The two status fields are preserved separately.
Results follow service order. Task/phase payloads, issues, identities and logs are
omitted. This command does not start or cancel deployments.

--limit/--all bound output across environments. Empty deploySteps arrays are complete;
missing/null arrays make completeness unknown. --require-complete keeps results but
returns exit 10 for unknown or truncated output. There is no continuation token,
and the 4 MiB response ceiling applies. Completeness concerns the embedded arrays,
not an independent audit of all historic deployment attempts. Mock tests cover routes,
bounds, invalid identities/payloads, redaction and CLI completeness. The user verified
release 1492 returned step 3615, deployment 1506, environment 1499 (ib-tasks),
attempt 1, status failed, operationStatus PhaseFailed and hasStarted true. This
identifies a deployment-phase failure but does not identify the failed task or cause.

## Task results

```powershell
dotnet run --project src/Ado.Cli --configuration Release -- release tasks --config ./config.json --release-id 1492 --token-prompt --output table --read-only --limit 100
```

This uses Get Release with $expand=tasks and traverses deployment attempts, phases,
jobs and their task arrays. Rows include task name/status/line count and the release,
environment, step, deployment, attempt, phase and job context. Task IDs are scoped to
their job; the same ID may occur in other jobs or attempts. Service order is preserved.
Agent identities, task inputs, issues, result-code text and log URLs are omitted.
No log URLs are followed and no tasks are executed.

The local --limit/--all applies across all returned tasks. The 4 MiB response ceiling
still applies to the expanded response. Empty arrays are complete for this snapshot;
missing/null nested arrays yield unknown completeness. --require-complete retains
data with exit 10 for unknown or truncated results. No continuation is available.
Gate evaluation tasks and parent-job summary rows are outside this command's scope.
Mock tests cover the expansion query, context, bounds, malformed data, identity checks,
redaction and terminal escaping. The user verified ten task records for release 1492,
environment 1499, deployment 1506, attempt 1. Task 12 (Setting up 200compute) reported
failed; the other nine tasks reported succeeded. Line counts were absent. The task
summary identifies the failure location, not its underlying cause.

## Task log content

```powershell
dotnet run --project src/Ado.Cli --configuration Release -- release task log --config ./config.json --release-id 1492 --environment-id 1499 --deployment-id 1506 --task-id 12 --token-prompt --output table --read-only --limit 100
```

The command first expands release tasks to resolve the requested environment,
deployment and task to exactly one phase ID. Missing metadata or ambiguous matches
fail before the log request. Task summaries now include phaseId in JSON. The log
URL is constructed from validated IDs; returned logUrl fields are never followed.
The service's string phaseId is normalized to a positive int32 (numeric JSON is
also accepted). Invalid phase values produce a field-specific error without
echoing the value. This corrects a parser mismatch identified after the first
user-reported task-log attempt. The user subsequently verified successful retrieval
for release 1492, environment 1499, deployment 1506, task 12.

Get Task Log returns UTF-8 text/plain. JSON output contains context and lines; table
output prints escaped lines. The active credential is redacted from the full text
before splitting into lines. Other secrets in logs are not guaranteed to be redacted.
HTML, invalid UTF-8, unexpected continuations and redirects are refused. Each request
has a 4 MiB response ceiling and the existing bounded read retry/deadline policy.
Metadata resolution is bounded by response size; --limit/--all bounds displayed lines.

--start-line and --end-line pass nonnegative service positions unchanged. Any explicit
range marks full-log completeness unknown; local truncation marks it partial.
--require-complete retains the lines and returns exit 10 in either case. No log file
is written, and no deployment actions occur. Mock tests cover phase resolution,
routes, ranges, limits, invalid content and safe output. The user verified table
output with --limit 100 through the task's finishing marker, with escaped terminal
controls and no truncation warning. The historical log (2022-02-17) reported AWS
STS ExpiredToken during Terraform S3 backend initialization, followed by a plan
failure because the backend was not initialized and Bash exit code 1. Raw logs
and credential-related values are not stored in the repository. Live JSON output,
line ranges and strict-completeness behavior remain covered by mocks only.

References:

- [Releases List](https://learn.microsoft.com/en-us/rest/api/azure/devops/release/releases/list?view=azure-devops-rest-7.1)
- [Get Release](https://learn.microsoft.com/en-us/rest/api/azure/devops/release/releases/get-release?view=azure-devops-rest-7.1)
- [Get Task Log](https://learn.microsoft.com/en-us/rest/api/azure/devops/release/releases/get-task-log?view=azure-devops-rest-7.1)
