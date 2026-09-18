# Configuration

Use `ado config paths` to inspect the resolved location without opening a file or
retrieving credentials. `ado config show` validates and prints configuration;
`ado config show --effective` resolves the selected profile and overrides.

| Platform | Default file |
|---|---|
| Windows | `%APPDATA%\ado\config.json` |
| macOS | `~/Library/Application Support/ado/config.json` |
| Linux | `${XDG_CONFIG_HOME:-$HOME/.config}/ado/config.json` |

Relative XDG_CONFIG_HOME values are ignored. Explicit --config and ADO_CONFIG paths
may point to a repository-local `config.json`, which Git ignores. This file is not
auto-discovered: use `ado config show --config ./config.json --effective` or explicitly
set ADO_CONFIG. Keep the token in a credential provider; an environment reference uses
ADO_TOKEN. Ignoring a file does not encrypt its contents or enable plaintext-token support.

Explicit --config and ADO_CONFIG paths
may be relative to the invocation directory. An absent default file uses defaults;
an absent explicit file is an error. Files are limited to 1 MiB and JSON depth 32.
Comments, trailing commas, duplicate properties, unknown properties and invalid values
are rejected. JSON errors report a one-based line and UTF-8 byte column without echoing
parser messages or sensitive input. Schema validation errors describe the constraint.

```json
{
  "schemaVersion": 1,
  "defaultProfile": "work",
  "profiles": {
    "work": {
      "organization": "example-org",
      "project": "Example Project",
      "authentication": {
        "type": "pat",
        "provider": "macos-keychain",
        "service": "ado/example-org",
        "account": "azure-devops-pat"
      },
      "output": "json",
      "pagination": { "pageSize": 100, "limit": 100, "maxItems": 10000 },
      "timeouts": { "requestSeconds": 60, "operationSeconds": 300 },
      "downloads": { "maxBytes": 1073741824, "timeoutSeconds": 600 }
    }
  }
}
```

Credential references use the [authentication providers](authentication.md). `type`
selects pat or entra-token. `provider` names the backend; `service` names the credential
service; `account` is its lookup key, not necessarily an email address. Providers:
environment, stdin, prompt, windows-credential-manager, macos-keychain,
linux-secret-service. Native references require both service and account.
No plaintext token field is currently accepted; its explicit insecure opt-in is pending.
Configuration commands never retrieve credentials or read ADO_TOKEN.

## Precedence

1. File: --config > ADO_CONFIG > OS default.
2. Profile: --profile > ADO_PROFILE > defaultProfile.
3. Ordinary settings: explicit argument > environment > selected profile > defaults.
4. No default-profile inheritance when a different profile is selected.

Supported setting overrides: ADO_ORGANIZATION, ADO_PROJECT, ADO_OUTPUT, ADO_LIMIT,
ADO_TIMEOUT. Corresponding flags: --organization, --project, --output, --limit,
--timeout. --json selects JSON output. Sources in effective output describe the
resolved configuration layer; a normalized profile includes its built-in defaults.
Unknown profile selection fails. Changing organization with a native credential
reference is refused; select a profile bound to that organization.

Unix: configuration readable by group or others generates a warning on stderr.
Use `chmod 700` on the configuration directory and `chmod 600` on the file.
Windows: restrict access through NTFS ACLs using the Security tab in file properties.
Unix permission commands do not apply to Windows.

## Current global parameters

| Parameter | Meaning | Default | Interactive behavior | Automation guidance | Security notes |
|---|---|---|---|---|---|
| --help | Command syntax | Off | Prints help | Use explicit commands | No credentials accessed |
| --version | Clean semantic version | Off | Prints version | Pin version | No build hash |
| --config | Config path | OS path | No prompt | Explicit path recommended | Strict JSON, bounded read |
| --profile | Named profile | defaultProfile | No prompt | Specify intended profile | No credential inheritance |
| --organization | Organization context | Profile/environment | No prompt | Specify explicitly | Native reference binding enforced |
| --project | Project context | Profile/environment | No prompt | Specify explicitly | No URL construction here |
| --output | table/json | table/profile | No prompt | Use json | Diagnostics stderr |
| --json | JSON alias | Off | No prompt | Stable envelope | No token values |
| --limit | Item result limit | 100 | No prompt | Set bounds | Cannot exceed profile ceiling |
| --timeout | Request seconds | 60 | No prompt | Set bounds | Positive and <= operation timeout |
| --auth-type | pat/entra-token | Profile/env, otherwise pat | No inference | Specify token type | Opaque token handling |
| --token | Direct token | None | Warns on stderr | Avoid | Process/history exposure |
| --token-stdin | Read one token to EOF | Off | Deliberate stdin read | Preferred secret injection | Bounded; one terminal newline removed |
| --token-prompt | Masked token input | Off | Requires terminal | Never use unattended | Refused in JSON/non-interactive mode |
| --credential-provider | Backend | Env/profile/environment | Backend-specific | Native store or env/stdin | No shell provider |
| --credential-service | Native service/target | Selected reference | No prompt | Stable key | Explicit provider requires complete reference |
| --credential-account | Native account key | Selected reference | No prompt | Stable key | Not necessarily email |
| --non-interactive | Suppress all prompts | Off; implicit with JSON | Never prompts when set | Set explicitly | Includes keyring unlock/access prompts |
| --read-only | Block remote writes centrally | Off | No prompt | Use for discovery | Blocks run submission; permits local dry-run |
| --dry-run | Prevent mutation dispatch | Off | Local plan for run start/preview | Reads may still execute | Start/preview retrieve no credentials and send no HTTP |

Project list/search also support --top, --all, --continuation-token and
--require-complete. Search requires --name. See [project command contracts](projects.md).
Pipeline run start/preview require exact --confirm for server requests and optionally
accept --ref, --parameters-file and --variables-file. Preview additionally accepts
--show-yaml (expanded content can contain secrets). See [pipeline contracts](pipelines.md).
Build list supports paging plus --definition-id, --status, --result and --branch;
build get requires --build-id. Build logs requires --build-id; build log get additionally
requires --log-id and accepts --start-line/--end-line. Log commands accept --limit,
--all and --require-complete, but no paging tokens. See [build contracts](builds.md).
Build artifact list/get require --build-id; get also requires --artifact-name. List
supports --limit, --all and --require-complete without server pagination. These commands
inspect build-output metadata only; download links are not returned or followed.
Build artifact download also requires --destination and optionally accepts --max-bytes
and --download-timeout to lower profile download ceilings. --dry-run is local only;
--read-only permits the remote GET and explicitly requested local file. Existing files
are never overwritten. The operation deadline remains an additional download bound.

`--version` always prints only the semantic version, even with --json. Config text
output uses indented JSON for readability. Schema errors exit 3; command syntax errors
exit 2; cancellation exits 130 unless a write may have been delivered (uncertain_write,
exit 8). Successful local inspection exits 0.
