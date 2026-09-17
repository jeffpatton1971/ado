# Pipelines and runs

These commands use Azure DevOps Services Pipelines REST API 7.1 to inspect definitions
and runs or submit a run. Build API records and classic Releases have separate commands.
The read endpoints document `vso.build`; resource access is also required.
`auth check` tests project access and does not prove pipeline permission.

```text
ado pipeline list --config ./config.json --token-prompt --output table --read-only --limit 20
ado pipeline get --config ./config.json --pipeline-id 12 --token-prompt --output table --read-only
ado pipeline runs --config ./config.json --pipeline-id 12 --token-prompt --output table --read-only --limit 20
ado pipeline run get --config ./config.json --pipeline-id 12 --run-id 34 --token-prompt --output table --read-only
```

For source builds, substitute `dotnet run --project src/Ado.Cli --configuration Release --`
for `ado`. Replace sample IDs with real IDs discovered by the preceding commands.
Organization/project resolve from --organization/--project, environment or selected
profile. A project is required for all pipeline commands. IDs must be positive integers.

For automation, use `--json --non-interactive --read-only` with token stdin, injected
ADO_TOKEN or an OS credential reference. JSON mode does not allow token prompts.
Command-line tokens remain discouraged. Human tables escape terminal control characters.

## Definitions

`pipeline list` supports --top, --limit, --all, --continuation-token and --require-complete.
The continuation token is opaque, unlike the numeric Core project offset. It is encoded
as a query value, never interpreted as a URL. Default limit/page size is 100; --all uses
the configured item ceiling (10,000 by default). An explicit --limit cannot accompany --all.
Additional bounds are 100 pages, 4 MiB per decompressed response and a 64 MiB aggregate
response threshold checked between pages. Repeated continuation tokens fail safely.

The Pipelines API can report configuration types such as yaml, designerJson and
justInTime. Output preserves the reported type; an omitted configuration is labeled
unknown. Discovery does not assume every returned definition is YAML or perform one
detail request per item. JSON definition fields: id, name, folder, revision,
configurationType. `pipeline get` returns one definition at its current revision.

## Runs

`pipeline runs` accepts --limit, --all and --require-complete. Microsoft documents a
10,000-run server cap and no page-size/continuation parameters. Accordingly --top and
--continuation-token are rejected for this command. --limit is a client-side output
bound; it cannot reduce the API response size. The shared 4 MiB response guard can fail
before a very large run response can be inspected. --all cannot fetch beyond the server cap.

If more runs arrive than the selected limit, meta.truncated is true with item_limit.
If 10,000 runs arrive, completeness is unknown and truncationReason is server_limit;
older history cannot be ruled out. No continuation token is invented. Strict completeness
returns exit 10 and ok:false while preserving the safely parsed partial data. Otherwise
bounded reads return exit 0 with explicit metadata/warnings. Empty lists succeed.

`pipeline run get` requires both pipeline and run IDs and verifies their relationship
against the response. JSON run fields: id, name, pipelineId, state, result, createdDate,
finishedDate. Variables, template parameter values, resource payloads, signed/other URLs
and expanded final YAML are omitted; raw service payloads are never echoed.

Both list commands preserve server order. Successful get responses are objects; list
responses are arrays in the version-1 envelope. Errors follow the shared exit-code
contract. --dry-run on these read commands still allows reads and cannot mutate.

## Start a run

`pipeline run start` submits one run using the Pipelines Run Pipeline 7.1 endpoint.
The endpoint documents `vso.build_execute`; project access alone does not grant queue
permission. Running a pipeline can deploy software or incur costs according to its YAML.

First inspect the local plan; this command needs no token and makes no HTTP requests:

```text
ado pipeline run start --config ./config.json --pipeline-id 12 --dry-run --read-only --json
```

The output includes organization, project, pipeline ID, optional self-repository ref,
parameter/variable counts and `requiredConfirmation`. It omits all input values and
input names. It does not verify pipeline existence, permissions or YAML, and it is not
the server's `previewRun` feature. Server preview remains unimplemented.

For an intended execution, repeat the command without --dry-run/--read-only and supply
`--confirm` with the exact `requiredConfirmation` string. Confirmation is required in
every mode; there is no --yes bypass or interactive confirmation prompt. Example for
an explicitly selected disposable target:

```text
ado pipeline run start --organization example --project Sandbox --pipeline-id 12 --token-prompt --output table --confirm "pipeline run start:example/Sandbox/12"
```

`--read-only` blocks actual submission before credential acquisition and again at HTTP
dispatch. It can accompany the local dry-run. Successful output includes only `id`,
`pipelineId` and `submitted`; use `pipeline run get` to inspect execution. Submission
success does not mean the pipeline completed successfully.

Optional inputs (use identical options for dry-run and execution):

- `--ref refs/heads/main` or `--ref refs/tags/v1.0` sets `resources.repositories.self.refName`.
  Omission uses the service's configured defaults; the CLI does not resolve a commit.
- `--parameters-file PATH` supplies a JSON object as `templateParameters`, preserving
  JSON types such as booleans, numbers, arrays and nested objects. There is no invented
  `runtimeParameters` request field.
- `--variables-file PATH` supplies a map such as
  `{"mode":{"value":"test"},"password":{"value":"...","isSecret":true}}`.
  Values must be strings; isSecret is optional and must be boolean. Mark sensitive
  variables as secret for service-side handling. All variable values are omitted from
  CLI previews regardless of isSecret. Parameter values are not service secret variables.

Input files are limited to 256 KiB each, depth 16 and 256 top-level entries. Duplicate
keys and unknown variable fields are rejected. The total serialized request is capped
at 1 MiB. Input files are user-managed and not automatically ignored by Git: keep files
containing secrets outside the repository. The CLI never writes or logs input values.
Other resource overrides, skipped stages, revision pinning and YAML overrides are not
exposed in this command yet.

POST is attempted once, with no retry. Transport errors, timeout/cancellation after
dispatch, ambiguous HTTP failures, oversized responses or an invalid success response
return `uncertain_write`, exit 8 and retryable:false. A run may have been created.
Inspect `pipeline runs` using the same organization/project/pipeline and check Azure
DevOps before deciding whether to submit again. Listing cannot prove non-delivery or
uniquely identify a submission when other users are queueing runs. Cancellation before
dispatch sends nothing. Authentication/authorization and other explicit refusals retain
their normal error codes. Neither redirects nor raw error bodies are followed/displayed.

## Verification

Mocked tests cover all four endpoint URLs, project routing, IDs, opaque pagination,
repeated continuation refusal, client/server bounds, missing inputs, sensitive-field
omission, JSON and strict completeness. Submission tests cover exact confirmation,
read-only/dry-run guards, typed requests, secret omission, input bounds, single-attempt
errors and uncertain delivery. On 2026-09-17 the user reported successful live pipeline
listing and run-history retrieval for impldevmpc, in addition to Core project checks.
Individual pipeline/run get and run submission remain unverified against the live service.
The development agent has made no live Azure DevOps requests or pipeline submissions.

Sources, verified before implementation:

- [Pipelines List](https://learn.microsoft.com/en-us/rest/api/azure/devops/pipelines/pipelines/list?view=azure-devops-rest-7.1)
- [Pipelines Get](https://learn.microsoft.com/en-us/rest/api/azure/devops/pipelines/pipelines/get?view=azure-devops-rest-7.1)
- [Runs List](https://learn.microsoft.com/en-us/rest/api/azure/devops/pipelines/runs/list?view=azure-devops-rest-7.1)
- [Runs Get](https://learn.microsoft.com/en-us/rest/api/azure/devops/pipelines/runs/get?view=azure-devops-rest-7.1)
- [Run Pipeline](https://learn.microsoft.com/en-us/rest/api/azure/devops/pipelines/runs/run-pipeline?view=azure-devops-rest-7.1)
