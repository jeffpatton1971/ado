# Changelog

## Unreleased

- Add registered project endpoints, organization/host validation, redirect refusal,
  bounded read retries and responses, typed project results and continuation handling.

- Add native credential adapters and setup guidance for Windows Credential Manager,
  macOS Keychain and Linux Secret Service, with non-interactive prompt suppression.

- Add opaque PAT/Entra authentication abstractions and bounded environment/stdin/masked-prompt providers (CLI wiring follows).

- Record approved Azure DevOps Services CLI design and threat model.
- Adopt `ado` command name, `ado` configuration directory and `ADO_` environment prefix.
- Add .NET 10 solution, centralized versioning, CLI help/version and safe JSON usage errors.
- Add strict bounded configuration parsing, OS-specific paths, isolated named profiles,
  deterministic overrides, credential-reference validation and configuration diagnostics.
- Preserve JSON envelopes for cancellation and unexpected errors.

No release has been published. Initial development targets version 0.1.0.
