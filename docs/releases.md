# Classic release inspection

`release list` and `release get` inspect classic Azure DevOps releases, separate from
YAML pipeline runs and builds. Both require an organization and project and use
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
Truncated results retain their continuation token; --require-complete retains data
but returns exit 10. An empty complete list succeeds. Get validates the requested ID.

--read-only is supported. As with other metadata reads, --dry-run still reads the
service. No create, deploy, approve or cancel commands are implemented here.

Mock tests cover routing, pagination, redaction, project/definition/release identity,
invalid continuations and CLI output. The user verified live listing in impldevmpc:
20 releases were returned with a truncation warning, including release 1492 from
definition 20. Individual release retrieval and continuation resume remain unverified.

References:

- [Releases List](https://learn.microsoft.com/en-us/rest/api/azure/devops/release/releases/list?view=azure-devops-rest-7.1)
- [Get Release](https://learn.microsoft.com/en-us/rest/api/azure/devops/release/releases/get-release?view=azure-devops-rest-7.1)
