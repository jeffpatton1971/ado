# Approved design

Approved 2026-09-17, with the executable renamed to `ado` and small incremental
commits explicitly requested. Package: `PattonTech.Ado.Cli`. Configuration directory:
`ado`; environment prefix: `ADO_`. The name overlaps other tools; this is a deliberate
user choice. Existing AGPL license is retained.

Delivery priorities revised 2026-09-17 to the user's BAT requirements sheet:
authenticated read-only diagnostics first. The [requirements backlog](roadmap.md)
is authoritative for milestone order, feature gaps and completion criteria.

## Boundaries

Azure DevOps Services only. C#/.NET 10 LTS. CLI -> Application -> Domain;
Infrastructure implements configuration, typed REST clients, endpoint construction,
pagination and download adapters; Platform implements native credential access.
No shell invocation for credentials or service requests. Async I/O and cancellation.
Self-contained distribution is the runtime-independent installation option.

Distinct models: YAML pipeline definition, pipeline run, build, classic release
definition, classic release, build output, feed, package and package version.
YAML stages/environments are not classic releases. No generic REST command.

## Configuration and credentials

Strict JSON. Path: --config > ADO_CONFIG > OS default. Profile: --profile >
ADO_PROFILE > defaultProfile. Settings: flags > environment > selected profile >
defaults. Profiles never inherit another profile's credentials. Credential references
are atomic; missing explicit credentials never silently fall back.

Default config.json directories: Windows APPDATA/ado; macOS Library/Application
Support/ado; Linux XDG_CONFIG_HOME/ado or ~/.config/ado. No repository-local discovery.
Config output omits secrets. Unix broad readability warns; plaintext credentials
are rejected and ado does not persist tokens. Windows uses NTFS ACLs.

PAT Basic with empty username and externally supplied Entra Bearer tokens. Tokens
remain opaque. Sources: explicit argument (warning), stdin, masked prompt, environment,
native Windows Credential Manager/macOS Keychain/Linux libsecret.
Provider, service and account identify the credential; account is not an email rule.
Noninteractive mode prevents provider unlock/access prompts. No token telemetry,
logging, URL placement, hashing, claim inspection or persistence by default.

## Transport and safety

Central operation registry: read/write, service, version, context, confirmation,
retry, scope guidance, pagination, output schema and official endpoint source.
dev.azure.com for Core/Pipelines/Build; vsrm.dev.azure.com for classic Release;
feeds.dev.azure.com for feed/package metadata. Exact service/organization credential
binding; no configurable production host override. Encode individual path segments.
Disable automatic redirects. Signed downloads use isolated unauthenticated HTTPS
transport with destination and redirect validation. Never print signed URLs.

Read-only enforced before use cases and at dispatch. Dry-run previews a sanitized
request without mutation; reads may resolve targets. Confirmation includes organization,
project, IDs and action. --yes cannot bypass exact confirmation. JSON never prompts.
Server-side pipeline preview is separate from dry-run and initially blocked read-only.
Reads have bounded retries/Retry-After/jitter. Writes never automatically retry.
Ambiguous write delivery returns uncertain_write plus reconciliation guidance.

Pagination is endpoint-specific: default 100 items, ceiling 10,000, bounded pages,
bytes and elapsed time. --all remains bounded. Unknown completeness is explicit.
Pipeline Runs List documents a 10,000-run cap with no paging parameters. Package
provenance requires 7.1-preview.1 and explicit preview opt-in. Pipeline runtime YAML
parameters map to templateParameters; do not invent a runtimeParameters REST field.

Downloads require an explicit destination, size/time bounds, no accidental overwrites,
safe filesystem paths and temporary-to-final rename. Opt-in bounded archive inspection
and safe extraction are M1 requirements, not implemented capabilities. Logs are
bounded with escaped terminal controls and no automatic retention. Evidence export
uses selected sanitized fields; arbitrary raw logs are not assumed secret-free.

## Output and delivery

Stable JSON envelope: ok/data/meta or ok/error, schema version 1. Diagnostics stderr.
Exit codes: 0 success; 1 internal; 2 usage; 3 config; 4 authentication; 5 authorization;
6 not found; 7 safety/conflict; 8 uncertain write; 9 exhausted transient/timeout;
10 partial/strict completeness; 130 cancellation unless write outcome uncertain.

Milestones: (1) authentication plus run diagnostics, selected build evidence, exact
source discovery/provenance and feed/package inspection; (2) configuration, expanded
YAML, detailed tests/comparison, watching and richer evidence export; (3) read-only
agent/queue and environment/check diagnostics; (4) optional authorized execution.
Administrative writes, Azure Repos PR management and Boards CRUD are deferred.
Platform hardening, packaging, documentation and CI are release gates across milestones.
Existing classic-release and execution commands are retained; new work follows the
[ranked backlog](roadmap.md). Each milestone has small commits, tests, capability
and docs updates, and changelog updates. Never represent placeholders as working capabilities.

Version authority: Directory.Build.props. Clean semantic --version; metadata only
on diagnostic request. Tests never require developer credentials. CI covers Windows,
macOS and Linux, with executed versus cross-compiled architecture coverage explicit.

No pushes, PRs, merges, releases, publication, live access, or remote mutations are
authorized by design approval. Each needs the user's corresponding authorization.
