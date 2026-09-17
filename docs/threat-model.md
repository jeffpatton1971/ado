# Initial threat model

Trust boundaries: command/environment/config -> validated context; credential backend
-> authenticated transport; remote JSON/logs -> output; download network -> filesystem.
Azure DevOps responses and local config are untrusted. No telemetry is enabled.

| Threat | Required control | Verification |
|---|---|---|
| Token exposure in arguments/history | Warn on --token; recommend stdin/keyring | CLI stderr test |
| Token exposure in errors/config/crashes | Safe error catalog, allowlisted output DTOs, no raw exceptions or dumps | Secret sentinel tests |
| Credential forwarding/SSRF | Exact host/org binding; isolated signed URL transport; redirects off | Host/redirect tests |
| Malicious org/project | Validate org and encode individual path segments | URL contracts |
| Profile confused deputy | Atomic auth references, no profile inheritance, organization binding | Resolution tests |
| Tampered config/provider spoofing | Strict schema, provider allowlist, no executable providers; permission checks | Config/platform tests |
| Keychain first-use/relocation | Native prompt suppression; actionable safe errors; setup docs | Adapter and manual OS tests |
| Headless keyring unavailable | Explicit failure; env/stdin alternatives, no implicit fallback | Platform tests |
| Hostile logs/metadata | Escape controls, bounded content/depth, no automatic URL resolution | Adversarial fixtures |
| Pagination/resource exhaustion | Item/page/byte/time ceilings; cancellation | HTTP tests |
| Accidental/costly mutation | Descriptor policy + dispatch guard, exact action/target confirmation | Every mutation enumerated in tests |
| Duplicate/uncertain writes | No write retries; uncertain_write and reconciliation | Response loss tests |
| Signed URL leakage | Omit URLs from output/errors; no auth on content transport | Sentinel/header tests |
| Filesystem escape/overwrite | Validate ancestors; no implicit extraction; no-clobber finalization | Filesystem attack tests |
| Partial content mistaken complete | Bounded temp download and atomic completion | Interrupted transfer tests |
| CI secret misuse | No live credentials in tests; no log/env dumps | CI review |

Residual risks: process administrators can inspect memory/environment; terminal history
may capture explicit token arguments; native OS stores have platform-specific trust and
availability; source builds can invalidate Keychain authorization. Do not claim secure
erasure of managed strings. Remote mutation success does not imply deployment success.
