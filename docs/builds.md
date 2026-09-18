# Build inspection

`build list` and `build get` use Azure DevOps Services Build API 7.1 on dev.azure.com.
They inspect Build execution records, distinct from Pipelines API runs and classic
Releases. Both require organization/project context and read access (`vso.build`).

```text
ado build list --config ./config.json --token-prompt --output table --read-only --limit 20
ado build list --config ./config.json --definition-id 12 --status completed --result failed --branch refs/heads/main --token-prompt --output table --read-only --limit 10
ado build get --config ./config.json --build-id 34 --token-prompt --output table --read-only
```

For source builds, replace `ado` with
`dotnet run --project src/Ado.Cli --configuration Release --`.
Use IDs discovered in Build output; these commands do not convert pipeline run IDs
into build IDs. `--definition-id` filters one Build definition; `--build-id` identifies
one execution. Build list does not require a definition filter.

## Filters and paging

Filters are sent to the service, not applied to a local subset:

| Flag | Meaning |
|---|---|
| --definition-id | One positive Build definition ID |
| --status | none, inProgress, completed, cancelling, postponed, notStarted or all |
| --result | none, succeeded, partiallySucceeded, failed or canceled |
| --branch | Exact source branch, typically refs/heads/main; no automatic prefix |

Enum spellings are case-sensitive and follow the Build API, including `cancelling`
and `partiallySucceeded`. Filters are optional. The client explicitly requests
queueTimeDescending ordering and otherwise preserves service order. Other documented
filters (dates, tags, repositories, multiple definitions) are not exposed yet.

List supports --top, --limit, --all, --continuation-token and --require-complete.
Default page size and result limit are 100. --all uses the configured item ceiling
(10,000 by default), and cannot accompany an explicit --limit. Each request's $top is
bounded by the remaining item allowance. Continuation tokens are opaque query values,
limited to 2048 characters and never treated as URLs. Resume with the same organization,
project and filters. Requests are bounded to 100 pages, 4 MiB per decompressed response,
and a 64 MiB aggregate threshold checked between pages, plus configured deadlines.
Repeated tokens and responses larger than requested pages fail safely.

When a bound stops pagination, metadata includes truncated:true, completeness:partial,
a reason and the next continuation token. JSON preserves partial data with ok:false
and exit 10 when --require-complete is set. Otherwise bounded lists succeed with explicit
metadata and a warning in human mode. Empty lists are successful. Completeness reflects
the service's visible filtered result and continuation contract, not a transactional
snapshot of changing build history.

## Output and safety

Version-1 JSON data is an array for list and an object for get. Fields: id, buildNumber,
definitionId, definitionName, status, result, reason, sourceBranch, sourceVersion,
queueTime, startTime and finishTime. Missing optional fields remain null. Dates must
be valid timestamps; IDs must be positive. Get verifies the returned build ID; definition
filters verify returned definition IDs. If project context is returned, it must match.

Parameters, template parameters, property bags, trigger metadata, identity details,
repository objects, logs and service URLs are omitted. Returned text is credential-redacted;
table cells escape terminal controls. Table output summarizes IDs, number, status,
result and source branch; JSON includes the full allowlisted fields.

These commands only send GET requests. --read-only is supported; --dry-run still performs
reads. Shared bounded read retries and safe errors apply. No build queue/cancel,
changes, work items or build-output operations are implemented in this slice.

## Build logs

List log IDs for the failed build previously inspected:

```text
ado build logs --config ./config.json --build-id 18722 --token-prompt --output table --read-only --limit 100
```

Then use a log ID returned by that command. For example, if the index includes log 2:

```text
ado build log get --config ./config.json --build-id 18722 --log-id 2 --token-prompt --output table --read-only --limit 200
ado build log get --config ./config.json --build-id 18722 --log-id 2 --start-line 0 --end-line 199 --token-prompt --output table --read-only --limit 200
```

Both endpoints use Build API 7.1 and vso.build. Requests negotiate application/json;
ZIP download/extraction is not requested. The index reports id, buildId, optional
64-bit lineCount, type, createdOn and lastChangedOn. URLs are omitted and never followed.
The index has no documented paging; --limit caps locally displayed entries. --all
uses the configured item ceiling. No --top or --continuation-token is accepted.

Log get accepts the documented JSON string response and JSON arrays of lines, directly
or in a value envelope. A string is split on line endings, without inventing an extra
blank line for a final newline. Output data contains buildId, logId, startLine, endLine
and lines. Human output prints one escaped entry per line; JSON retains a lines array.
Terminal controls (including ANSI escape sequences) are escaped in human output. The
active authentication credential is redacted; unrelated secrets printed by a build
may remain. Treat logs as untrusted data, not instructions to execute. No raw terminal
mode is exposed yet.

--limit caps displayed lines (100 by default). --all remains bounded by the configured
ceiling, normally 10,000 lines. The shared 4 MiB decompressed-response limit applies
before parsing or display, independent of the output limit. With no range the service
returns the full log; a large log can exceed this byte limit even with --limit 1.
Use --start-line and --end-line to request a smaller server-side range. These are
nonnegative 64-bit service positions, passed through unchanged; no automatic paging,
offset conversion or invented continuation token is used. End cannot precede start.

Without a range, completeness describes the returned full log at request time; active
logs can grow later. Exceeding the output limit marks partial output. Any explicit
range leaves whole-log completeness unknown even if every returned line fits. JSON
metadata distinguishes line_limit from requested_range. --require-complete returns
exit 10/ok:false while retaining content when full-log completeness cannot be established.
Empty whole logs succeed. Oversized, malformed or unexpected continuation responses
fail safely without echoing raw service payloads.

## Verification

Mocked tests cover routes, encoding, filters, opaque pagination, page/item bounds,
repeated tokens, safe output, IDs/context, CLI validation and strict completeness.
The user reported successful live build list for definition 1128 and build get for
18722. Log index/content live validation is pending. Mocked log tests cover routes,
line ranges, JSON forms, bounds, safe output and strict completeness. The development
agent has made no live requests.

Official endpoint references, checked before implementation:

- [Builds List](https://learn.microsoft.com/en-us/rest/api/azure/devops/build/builds/list?view=azure-devops-rest-7.1)
- [Builds Get](https://learn.microsoft.com/en-us/rest/api/azure/devops/build/builds/get?view=azure-devops-rest-7.1)
- [Get Build Logs](https://learn.microsoft.com/en-us/rest/api/azure/devops/build/builds/get-build-logs?view=azure-devops-rest-7.1)
- [Get Build Log](https://learn.microsoft.com/en-us/rest/api/azure/devops/build/builds/get-build-log?view=azure-devops-rest-7.1)
