# Local artifact inspection

`artifact inspect` reads an existing ZIP downloaded by `build artifact download`
or another tool. It makes no service requests, loads no profile/configuration and
retrieves no credentials. It does not extract files. Member text is printed only
when explicitly requested with --show-text.

```powershell
dotnet run --project src/Ado.Cli --configuration Release -- artifact inspect --file "$env:TEMP\ado-CompiledOutputs-18522.zip" --output table --read-only --limit 100
```

Use `--entry` with an exact, case-sensitive path from the inventory to inspect and
hash one member. Directories have no content digest. Selection happens locally
after the full archive has already been downloaded; it does not reduce network
transfer. The file may need downloading again if the prior smoke-test ZIP was removed.

## Selected text

```powershell
dotnet run --project src/Ado.Cli --configuration Release -- artifact inspect --file "$env:TEMP\ado-CompiledOutputs-18522.zip" --entry "CompiledOutputs/src/plugin.json" --show-text --text-lines 100 --output table --read-only
```

--show-text requires one exact file entry of at most 1 MiB expanded size. The entire
member is hashed and decoded as strict UTF-8; an optional leading UTF-8 BOM is omitted
from display. Invalid UTF-8, NUL bytes, directories and oversized text fail without
returning content. There is no encoding guessing or fallback for binary files.
Use metadata/hash mode for those files. Content is not parsed or executed as JSON,
XML, scripts or templates, and valid UTF-8 alone does not establish semantic validity.

--text-lines defaults to 100 and accepts 1–10000; it requires --show-text. Line
endings are normalized into lines. Table output escapes terminal controls. JSON
entries gain textLines and textLineCount (null unless requested), preserving content
via JSON escaping. These strings remain untrusted input for downstream consumers.
Truncation reports line_limit; --require-complete returns exit 10 with retained
metadata, full member hash and selected lines. --limit still limits inventory entries.
The 1 MiB text ceiling also bounds a single long line. No selected text is written
to disk or automatically retained. Text may contain secrets: this local command
does not retrieve credentials or claim to redact arbitrary member content.

Tests cover opt-in behavior, BOM/line handling, full hashes with truncated output,
terminal/JSON escaping, empty text, invalid encodings, NULs and the expanded text
ceiling. The user verified --show-text table output for
CompiledOutputs/src/plugin.json from build 18522 on 2026-09-18. The complete
manifest was displayed without a truncation warning; archive and member hashes
matched the earlier inspections. This confirms selected UTF-8 text display, not
manifest semantics or the presence of its declared runtime assemblies. Live JSON
and truncated-text checks remain unverified.

## Hashes and bounds

The archive SHA-256 is always computed. `--expected-sha256` accepts an independently
obtained digest. A bounded in-memory snapshot ensures the bytes inspected match
the archive digest even if the source file changes. The option requires an
exact 64-digit archive digest; mismatch exits 7 before parsing entries. The JSON
field `expectedHashMatches` is null when no expected digest was supplied and true
when comparison succeeds. A computed digest alone is not authenticity verification.
Member hashes are computed only for an explicitly selected file. Listing metadata
does not establish integrity of every compressed member or validate package semantics.

The fixed inspection ceilings are 64 MiB archive size, 64 MiB per member, 256 MiB
declared total expanded size, 10000 entries and a 60-second deadline. The central
directory is parsed within the archive-size ceiling before its entry count is
checked. Actual selected-member reads are bounded and checked against declared size.
Cancellation is checked during hashing, reads and entry validation; synchronous ZIP
metadata parsing is not preempted. These inspection limits are independent of download
limits. Unsupported/encrypted ZIP features may fail with a safe archive error.

All entry names are checked before limiting output. Traversal/absolute paths,
backslashes, alternate-stream separators, control/format characters, trailing dots
or spaces, case-insensitive duplicate paths, file/directory conflicts and ZIP Unix
symlink entries are refused. Local file paths with reparse-point/symlink ancestors
are refused; Windows network/device/alternate-stream paths are unsupported. These
checks are for inspection, not a promise that the archive is safe to extract with
an unrelated tool. The separate extraction command below uses an explicit output
file rather than mapping archive paths into a directory tree.

Default output limit is 100, configurable only by `--limit` (1–10000) for this local
command. Config/profile and credential settings do not apply. `--json` or
`--output json` emits the stable envelope; `--non-interactive` and `--read-only`
are compatible. `--require-complete` returns exit 10 while retaining truncated
inventory data. Completeness describes the selected inventory, not all archive
contents or remote artifact provenance. JSON reports the total entry count even
when selecting one member. `--dry-run` still performs this local read.

Tests generate temporary ZIPs and cover digests, exact selection, unsafe names,
collisions, links, file/entry ceilings, invalid ZIPs, cancellation and partial JSON.
The user verified inventory of CompiledOutputs from build 18522 on 2026-09-18:
4,455,202 bytes, 270 entries, archive SHA-256
8e901a95c4fcbe67672bba1b63c47d5eac99c65344041a0feefb41c96b30c9e2.
With --limit 100, 100 entries were displayed and the truncation warning appeared.
The user also verified exact selection of CompiledOutputs/src/plugin.json:
527 expanded bytes, 302 compressed bytes, SHA-256
12d40f511ca9bcb1a1e3b30db29c78f1e196c307bed9c2253b67da36399a2f48.
The archive digest remained unchanged. This verifies inventory, archive hashing
and selected-member hashing; expected-hash comparison and JSON output remain
mock-verified only. The recorded digest is an
observation, not independent evidence of authenticity. File names may
themselves contain sensitive information; inspection output is not a sanitized
evidence export. No raw archive content is retained by the command.

## Extract one member

```powershell
dotnet run --project src/Ado.Cli --configuration Release -- artifact extract --file "$env:TEMP\ado-CompiledOutputs-18522.zip" --entry "CompiledOutputs/src/plugin.json" --destination "$env:TEMP\ado-plugin-18522.json" --output table --read-only
```

artifact extract requires one exact case-sensitive file entry and an explicit new
destination file in an existing directory. Directories, wildcards and bulk extraction
are not supported. Archive entry names never determine destination paths. This
command does not recreate directory trees, links, executable modes or timestamps.
It never executes the content. Binary members are supported within the existing
64 MiB member bound; text-only inspection limits do not apply to extraction.

The same full-archive validation, immutable in-memory snapshot, size/entry ceilings,
expected archive hash comparison and 60-second deadline apply. No temporary output
is created until validation and exact selection succeed. The selected bytes stream
to a CreateNew temporary file in the destination's directory while computing their
SHA-256. After length verification and flush/close, destination checks run again and
the file is renamed without overwrite. Cancellation is checked before publication.
Failure cleanup attempts to remove the temporary file; filesystem failures can
prevent cleanup. Parent symlinks/reparse points are refused. As with downloads,
path checks are not a sandbox against another process replacing parent directories
concurrently; use a directory under your control.

--dry-run validates and hashes the selected member and reports the intended
destination with dryRun:true/written:false, without creating output. Otherwise
the result reports entry, destination, bytes, archiveSha256, memberSha256 and
written:true. Table mode prints this result as a formatted object; JSON uses the
stable envelope. --read-only permits this explicitly requested local write, since
it cannot mutate Azure DevOps. No credentials/configuration or network are used.
Neither hashes nor successful extraction establish trusted publisher identity or
validate package semantics. Expected archive hash comparison is opt-in.

Tests cover selected bytes/digests, dry-run, overwrite refusal, unsafe archives,
missing/directory entries, hash mismatch and temporary cleanup after unsupported
compression. The command reuses inspection's bound tests and the download target's
path checks. On 2026-09-18 the user verified extraction of
CompiledOutputs/src/plugin.json from the build 18522 archive to a new local file:
527 bytes, dryRun:false and written:true. The reported archive and member digests
matched the preceding inspections. This is a user-reported successful write;
independent destination rehash, live overwrite refusal and live dry-run remain
unverified (synthetic tests cover those behaviors).
