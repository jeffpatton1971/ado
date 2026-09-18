# ado — Azure DevOps Services CLI

Development build of `PattonTech.Ado.Cli`; the executable is `ado`.
Azure DevOps Server/on-premises is not supported. The .NET tool requires .NET 10.

Includes project and pipeline discovery, build diagnosis/timelines/logs, build artifact
downloads and evidence, local archive inspection/extraction, classic release inspection,
feed/package discovery, exact NuGet download and local nuspec inspection. Named profiles,
service-specific access probes and JSON/non-interactive output support automation.

PATs and externally supplied Entra tokens can come from stdin, environment, masked
prompts or existing native credential-store items. Plaintext configuration tokens are
rejected. Automatic Entra login/refresh is not implemented. Native macOS/Linux store
validation remains open, separately from passing CLI tests/install checks on those OSes.

Run start and server YAML preview support credential-free local dry-run and require
exact confirmation before dispatch. --read-only blocks both remote actions while
allowing explicitly requested local downloads/extraction/evidence files. Downloads
never overwrite and isolate credentials from storage redirects. Hashes identify bytes,
not publisher authenticity. Build queue/cancel, dependency solving and administrative
writes are not implemented. M1 acceptance remains in progress.

For source installation, workstation setup, profiles, examples and release status,
see the [repository README](https://github.com/jeffpatton1971/ado/blob/feature/ado-foundation/README.md).

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
