# Authentication and native credential setup

`auth check` defaults to the project probe. Select `--capability build` with
`--build-id` for one build, `--capability artifact` with `--build-id` for its
artifact metadata list, or `--capability feed` with `--feed` and
`--scope project|organization` for one feed. Project scope is the default.
Required target context is validated before credential retrieval. Probes use
bounded GET clients without downloading content or testing write permissions.

```powershell
dotnet run --project src/Ado.Cli --configuration Release -- auth check --capability build --build-id 18722 --config ./config.json --token-prompt --output table --read-only
dotnet run --project src/Ado.Cli --configuration Release -- auth check --capability artifact --build-id 18522 --config ./config.json --token-prompt --output table --read-only
dotnet run --project src/Ado.Cli --configuration Release -- auth check --capability feed --scope organization --feed automation --config ./config.json --token-prompt --output table --read-only
```

Success means the selected metadata endpoint was accessible. Empty artifact lists
can confirm list access. Public resources do not independently prove credential
validity; not-found responses may mask visibility restrictions. JSON completeness
describes the probe, not a complete artifact inventory. Resource contents are omitted.

Token acquisition is separate from HTTP authentication. PAT uses Basic authentication
with an empty username. `entra-token` uses Bearer authentication; acquire the token for
Azure DevOps, not Azure Resource Manager. Tokens are opaque and never decoded. No
refresh or token acquisition through Azure CLI is performed. No email is needed for PATs.
Microsoft recommends Entra identities for new production integrations; PATs suit
personal/local and legacy ad hoc workflows. Managed identity, service principal and
workload federation acquisition are future provider work.

`--token` warns because process listings and shell history may expose it. Prefer
`--token-stdin`, a masked `--token-prompt`, `ADO_TOKEN` from CI secret injection, or a
native reference. Stdin reads to EOF, accepts one line and removes only one final LF
or CRLF. Other whitespace is preserved. Input is capped at 16,384 characters.
Non-interactive and JSON modes never open a token or native keyring prompt.
No implicit fallback occurs after a selected credential source fails.

## Windows Credential Manager

Open Control Panel > Credential Manager > Windows Credentials > Add a generic
credential. Set the Internet/network address to the profile's `service`, user name
to its `account`, and password to the token. `account` is a deliberate stable lookup
key; it need not be an email address. Provider: `windows-credential-manager`.
Lookup uses the service as the exact generic credential target and verifies its user
name against account. Password blobs must be UTF-16, as produced by this UI.

The credential belongs to the current Windows logon session. A scheduled task or
service running as another identity may not see it. Network logon sessions may lack
a credential set. Use injected environment/stdin credentials in those environments.
Windows file access is managed by NTFS ACLs, not chmod.

## macOS Keychain

Provider: `macos-keychain`. In Keychain Access create/import a generic password item
with Keychain Item Name matching `service`, Account matching `account`, and Password
containing the token. Use a deliberate unique service/account pair.

1. Create or import the item in an accessible keychain.
2. Run an interactive CLI service operation (without JSON/non-interactive flags).
3. macOS may ask whether this executable may access the item.
4. Choose **Always Allow** when unattended future access is intended.
5. Verify a subsequent operation with `--non-interactive`.
6. Broad item access settings may not substitute for approving the requesting executable.
7. Rebuilding or relocating the executable may change its identity and prompt again.

Unlock the keychain before headless execution. The adapter suppresses user interaction
in non-interactive mode and reports first-use guidance. It uses the longstanding generic
password API; this API is deprecated by Apple, so migration to SecItem APIs and actual
macOS validation remain platform-hardening work. A synchronous OS approval dialog cannot
be forcibly cancelled safely by the CLI; dismiss it to complete cancellation.

## Linux Secret Service

Provider: `linux-secret-service`. Requires `libsecret-1.so.0` and its GLib/GObject/GIO
dependencies, a session D-Bus, and a Secret Service implementation (for example the
desktop keyring). The CLI dynamically links the OS libraries; it does not invoke a shell.

Create/import a password item with string attributes `service` and `account` matching
the configuration. For manual setup, libsecret's optional `secret-tool` utility can
prompt for the secret (this is not a runtime dependency of ado):

```sh
secret-tool store --label='ado example-org' service 'ado/example-org' account 'azure-devops-pat'
```

No secret is placed in these command arguments. Keep the attribute pair unique:
multiple matches fail rather than selecting an arbitrary item. Interactive access may
unlock the keyring; non-interactive access omits libsecret's unlock flag. Unlock first,
then verify unattended access. Minimal containers/CI usually have no desktop keyring;
use secret-injected environment or stdin. Native calls receive a GCancellable.

## Verification status and limits

Adapter contracts and error mapping are tested with fake stores, without developer
credentials. Windows additionally round-trips a uniquely named synthetic generic
credential and deletes it in a finally block. Native macOS/Linux calls have not been executed on this Windows development
host; those integrations need target-OS validation before a supported release claim.
No credentials are stored by ado. Managed strings cannot guarantee secure memory erasure.
Plaintext configuration remains unimplemented and is rejected.

Sources: [Microsoft PAT guidance](https://learn.microsoft.com/en-us/azure/devops/organizations/accounts/use-personal-access-tokens-to-authenticate?view=azure-devops),
[CredRead](https://learn.microsoft.com/en-us/windows/win32/api/wincred/nf-wincred-credreadw),
[Apple Security headers](https://github.com/apple-oss-distributions/Security/tree/main/OSX/libsecurity_keychain/lib),
[libsecret source and search semantics](https://github.com/GNOME/libsecret/blob/master/libsecret/secret-methods.c).
