# ado — Azure DevOps Services CLI

Development build of `PattonTech.Ado.Cli`; the executable is `ado`.
Azure DevOps Server/on-premises is not supported. The .NET tool requires .NET 10.

Currently implements configuration inspection, isolated named profiles, project
list/get/search, pipeline definition/run inspection, guarded run submission, endpoint access
checks, server-side YAML preview, and local diagnostics. Run start supports local --dry-run and requires exact
--confirm before submission; uncertain delivery returns exit 8 without automatic retry. Authentication accepts
PATs or externally supplied Entra tokens through stdin, environment, masked prompt,
or native credential stores. Native macOS/Linux execution remains pending validation.
Server preview uses a separate confirmation and is blocked by --read-only; expanded
YAML is omitted unless --show-yaml is requested. Build list/get support bounded history
and safe execution metadata. Build logs/log get provide a bounded log index and
terminal-safe content, with optional service line ranges. Build artifact list/get inspect
output metadata without exposing download URLs. Build artifact download writes a bounded
ZIP to an explicit new file, without extraction, overwrite or credentials on storage
redirects. Live transfer validation is pending. Build queue/cancel and release/feed commands
are not implemented in this development build.

```text
ado --help
ado config paths
ado doctor --json
ado project list --organization example-org --token-stdin --json --non-interactive --read-only
```

Use least-privilege credentials and explicit context, limits and timeouts. Prefer stdin,
OS keyrings or CI secret injection; token command-line arguments can appear in process
listings/history. JSON output has stable success/error envelopes and bounded pagination.
Treat returned metadata as untrusted input and never execute it as shell commands.

This is an unpublished development package, not a complete initial release.
Licensed under GNU AGPL version 3; no warranty.
