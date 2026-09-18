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
changes or work-item inspection are implemented in this slice.

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

## Build-output metadata

`build artifact list` and `build artifact get` inspect outputs attached to a Build API
execution. These are not Azure Artifacts feed packages. Both use Build API 7.1, require
project context and `vso.build`, and only send GET requests accepting application/json.

```text
ado build artifact list --config ./config.json --build-id 18722 --token-prompt --output table --read-only --limit 100
```

Get selects an output by its exact name, not its numeric ID. If the list contains an
output named `drop`, inspect it with:

```text
ado build artifact get --config ./config.json --build-id 18722 --artifact-name drop --token-prompt --output table --read-only
```

The name is encoded as one query value and never interpreted as a path or URL. It must
be nonempty, at most 1024 characters and contain no controls. Get verifies that the
response name matches before redaction. JSON returns id, buildId, name, resourceType
and optional source (the producing job reference). Resource types are preserved as
reported; missing type is unknown. No type is assumed to be downloadable.

Resource data, property bags, download/service URLs and links are omitted and never
followed. No file is downloaded or written. Arbitrary resource properties do not imply
a universal size or file-count field. Text is credential-redacted and human table cells
escape terminal controls. JSON get returns one object; list returns an array.

List has no documented pagination. --limit bounds displayed outputs; --all uses the
configured ceiling. The full metadata response is still limited to 4 MiB before parsing.
No --top or --continuation-token is accepted. Empty lists succeed, including builds that
published no outputs. When the local limit truncates results, metadata is partial and
has no continuation token; --require-complete retains partial data but returns exit 10.
Unexpected continuation headers and malformed responses fail safely. --dry-run still
performs these metadata reads. ZIP download is a separate command below; archive extraction remains unimplemented.

## Build-output ZIP download

`build artifact download` requires --build-id, --artifact-name and --destination (a new
local file, not a directory). It first checks metadata and accepts Container and
PipelineArtifact resource types. It requests application/zip from the documented Build
Get Artifact endpoint. It does not follow the metadata's downloadUrl or guess resource
protocols. If the service does not support ZIP for that output, the command fails safely.

PowerShell example using the output already discovered, outside the repository:

```powershell
dotnet run --project src/Ado.Cli --configuration Release -- build artifact download --config ./config.json --build-id 18522 --artifact-name CompiledOutputs --destination "$env:TEMP\ado-CompiledOutputs-18522.zip" --token-prompt --output table --read-only
```

For a local plan, replace --token-prompt with --dry-run. Dry-run validates destination
and bounds without credential lookup, HTTP or file writes; it does not verify remote
availability. --read-only permits downloads: remote operations are GETs, and the explicit
destination authorizes the local write. No mutation confirmation is required.

The parent directory must exist. Existing files/directories are refused, with no
overwrite flag. Symlink/reparse-point ancestors, Windows network/device paths and
alternate streams are rejected. The response filename and archive entry paths are never
used as local paths. A randomly named .ado-*.partial file is created exclusively in
the same directory (0600 on Unix; inherited ACLs on Windows). After bounded transfer,
length checks and basic ZIP header/end-record checks, it is renamed without replacing
an existing destination. Archive entries are not parsed or extracted; this is not a
CRC/signature or malware verification. A downloaded archive remains untrusted content.

Use a destination directory controlled by your user. Ancestors are checked before
creation and finalization; managed path checks cannot prevent all races from another
process able to rename/replace those directories. Partial files are removed on failure
where possible, but process termination or filesystem permission changes can leave
the recognizable temporary file. Finalization is not a power-loss durability guarantee.

Profile downloads.maxBytes defaults to 1 GiB, downloads.timeoutSeconds to 600 seconds.
--max-bytes and --download-timeout can lower these ceilings. The overall operation
deadline also applies (300 seconds by default), including metadata/credential work;
--timeout governs the metadata read. Header sizes and streamed byte counts are checked.
Interrupted, oversized, partial-HTTP, compressed-HTTP, HTML/JSON or malformed ZIP responses
do not publish a completed file. Failure does not automatically retry or resume content.

Authentication is sent only to the exact constructed dev.azure.com organization/project
endpoint. Up to five redirects may target HTTPS port 443 artifact-storage hosts under
.vsblob.vsassets.io, .vsblob.visualstudio.com, .artifacts.visualstudio.com, .blob.core.windows.net or
.dedup.microsoft.com. Every hop is validated; IP literals, userinfo, fragments and other
hosts are refused. Redirected content uses a separate client with no authorization,
cookies, default credentials or referrer. Signed URLs and remote error bodies are never
printed. These suffixes are a deliberately narrow subset of Microsoft's networking
domains; an unfamiliar host fails instead of widening trust automatically.
Rejected redirects report only a bounded, credential-redacted DNS hostname for
diagnosis; URL paths, queries, user information and fragments remain omitted.

Success returns buildId, artifactName, destination, bytes and format:zip. Size overflow
uses exit 10; unsafe destination/type/redirect uses exit 7; transfer failure/deadline
uses exit 9; user cancellation uses exit 130. Service authentication/access/not-found
refusals retain exits 4/5/6. The response format is currently ZIP only.

## Verification

Mocked tests cover routes, encoding, filters, opaque pagination, page/item bounds,
repeated tokens, safe output, IDs/context, CLI validation and strict completeness.
The user reported successful live build list for definition 1128 and build get for
18722. The user also retrieved its 26-entry log index and log 3 (27 lines). Mocked log tests cover routes,
line ranges, JSON forms, bounds, safe output and strict completeness. The development
agent has made no live requests. Build-output metadata tests cover routes, name encoding,
identity checks, safe field selection, output bounds and CLI behavior. The user verified
an empty output list for build 18722, two PipelineArtifact outputs for 18522, and get by
name for CompiledOutputs (17112). A user-run download of that output was blocked by
redirect validation for artprodcus3.artifacts.visualstudio.com. That service suffix is
now allowed without forwarding credentials; successful live transfer remains unverified. Tests cover header
isolation, unsafe redirects and safe hostname diagnostics, redirect bounds,
byte limits, interruption cleanup, timeouts, ZIP envelope checks and overwrite refusal.
The synthetic symlink test skipped locally because the Windows session cannot create
symlinks; it remains part of the cross-platform suite.

Official endpoint references, checked before implementation:

- [Builds List](https://learn.microsoft.com/en-us/rest/api/azure/devops/build/builds/list?view=azure-devops-rest-7.1)
- [Builds Get](https://learn.microsoft.com/en-us/rest/api/azure/devops/build/builds/get?view=azure-devops-rest-7.1)
- [Get Build Logs](https://learn.microsoft.com/en-us/rest/api/azure/devops/build/builds/get-build-logs?view=azure-devops-rest-7.1)
- [Get Build Log](https://learn.microsoft.com/en-us/rest/api/azure/devops/build/builds/get-build-log?view=azure-devops-rest-7.1)
- [List Build Artifacts](https://learn.microsoft.com/en-us/rest/api/azure/devops/build/artifacts/list?view=azure-devops-rest-7.1)
- [Get Build Artifact](https://learn.microsoft.com/en-us/rest/api/azure/devops/build/artifacts/get-artifact?view=azure-devops-rest-7.1)
- [Azure DevOps allowed domains](https://learn.microsoft.com/en-us/azure/devops/organizations/security/allow-list-ip-url?view=azure-devops)
