# Pipeline discovery and run inspection

These commands use Azure DevOps Services Pipelines REST API 7.1. They read pipeline
definitions and pipeline runs, not Build API execution records or classic Releases.
The endpoint documentation lists the `vso.build` scope; resource access is also required.
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
contract. No pipeline execution, queueing, cancellation or preview POST is implemented
in this slice. --dry-run on these read commands still allows reads and cannot mutate.

## Verification

Mocked tests cover all four endpoint URLs, project routing, IDs, opaque pagination,
repeated continuation refusal, client/server bounds, missing inputs, sensitive-field
omission, JSON and strict completeness. No live pipeline access has been performed by
the development agent. The user's earlier live project checks established Core project
access only; pipeline access still needs its own explicitly initiated smoke test.

Sources, verified before implementation:

- [Pipelines List](https://learn.microsoft.com/en-us/rest/api/azure/devops/pipelines/pipelines/list?view=azure-devops-rest-7.1)
- [Pipelines Get](https://learn.microsoft.com/en-us/rest/api/azure/devops/pipelines/pipelines/get?view=azure-devops-rest-7.1)
- [Runs List](https://learn.microsoft.com/en-us/rest/api/azure/devops/pipelines/runs/list?view=azure-devops-rest-7.1)
- [Runs Get](https://learn.microsoft.com/en-us/rest/api/azure/devops/pipelines/runs/get?view=azure-devops-rest-7.1)
