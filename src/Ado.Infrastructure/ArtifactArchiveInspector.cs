using System.IO.Compression;
using System.Security.Cryptography;
using Ado.Domain;

namespace Ado.Infrastructure;

public sealed record ArchiveEntryInfo(string Path, bool Directory, long Bytes, long CompressedBytes, string? Sha256,
    IReadOnlyList<string>? TextLines = null, int? TextLineCount = null);
public sealed record ArchiveInspection(long ArchiveBytes, string Sha256, bool? ExpectedHashMatches, int TotalEntries,
    IReadOnlyList<ArchiveEntryInfo> Entries);
public sealed record ArchiveInspectionResult(ArchiveInspection Data, ResultMetadata Meta);
public sealed record ArchiveExtraction(string Entry, string Destination, long Bytes, string ArchiveSha256, string MemberSha256, bool DryRun, bool Written);

public static class ArtifactArchiveInspector
{
    internal static Task<ArchiveInspectionResult> InspectManifestAsync(string? path, string? expectedHash, CancellationToken cancellationToken) =>
        ProcessAsync(path, null, expectedHash, 1, cancellationToken, true, 10000, null, true);
    public static Task<ArchiveInspectionResult> InspectAsync(string? path, string? entryName, string? expectedHash, int limit, CancellationToken cancellationToken,
        bool showText = false, int textLines = 100) => ProcessAsync(path, entryName, expectedHash, limit, cancellationToken, showText, textLines, null);

    public static async Task<ArchiveExtraction> ExtractAsync(string? path, string? entryName, string? destination, string? expectedHash,
        bool dryRun, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(entryName) || entryName.EndsWith('/'))
            throw new AdoException("archive_entry_required", "Extraction requires one exact file --entry.", ExitCode.Usage);
        var target = DownloadTarget.Validate(destination);
        var result = await ProcessAsync(path, entryName, expectedHash, 1, cancellationToken, false, 100, dryRun ? null : target);
        var entry = result.Data.Entries.Single();
        return new(entry.Path, target.Path, entry.Bytes, result.Data.Sha256, entry.Sha256!, dryRun, !dryRun);
    }

    private static async Task<ArchiveInspectionResult> ProcessAsync(string? path, string? entryName, string? expectedHash, int limit, CancellationToken cancellationToken,
        bool showText, int textLines, DownloadTarget? target, bool manifest = false)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if ((showText && string.IsNullOrEmpty(entryName) && !manifest) || textLines is < 1 or > 10000)
            throw new AdoException("invalid_text_options", "Text inspection requires an exact --entry and --text-lines from 1 to 10000.", ExitCode.Usage);
        if (string.IsNullOrWhiteSpace(path) || limit is < 1 or > 10000 || (expectedHash is not null
            && (expectedHash.Length != 64 || expectedHash.Any(c => !Uri.IsHexDigit(c)))))
            throw new AdoException("invalid_archive_options", "Supply --file, a limit from 1 to 10000 and, optionally, a 64-digit --expected-sha256.", ExitCode.Usage);
        string? temporary = null;
        try
        {
            string fullPath = Path.GetFullPath(path);
            if (OperatingSystem.IsWindows() && (fullPath.StartsWith("\\\\", StringComparison.Ordinal) || fullPath[2..].Contains(':'))) throw Unsafe();
            _ = File.GetAttributes(fullPath);
            for (FileSystemInfo? current = new FileInfo(fullPath); current is not null; current = current is FileInfo file ? file.Directory : ((DirectoryInfo)current).Parent)
                if ((current.Attributes & FileAttributes.ReparsePoint) != 0) throw Unsafe();
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            deadline.CancelAfter(TimeSpan.FromSeconds(60));
            var ct = deadline.Token;
            await using var stream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, FileOptions.Asynchronous);
            const long maximum = 64 * 1024 * 1024;
            if (stream.Length > maximum) throw Bound();
            // Inspect precisely the bytes hashed, even if the source changes on another platform.
            using var snapshot = new MemoryStream();
            var archiveBuffer = new byte[65536];
            int archiveCount;
            while ((archiveCount = await stream.ReadAsync(archiveBuffer, ct)) != 0)
            {
                if (snapshot.Length + archiveCount > maximum) throw Bound();
                await snapshot.WriteAsync(archiveBuffer.AsMemory(0, archiveCount), ct);
            }
            snapshot.Position = 0;
            string digest = Convert.ToHexStringLower(await SHA256.HashDataAsync(snapshot, ct));
            if (expectedHash is not null && !string.Equals(expectedHash, digest, StringComparison.OrdinalIgnoreCase))
                throw new AdoException("archive_hash_mismatch", "The archive SHA-256 does not match the expected digest.", ExitCode.Safety);
            snapshot.Position = 0;
            using var archive = new ZipArchive(snapshot, ZipArchiveMode.Read, leaveOpen: true);
            if (archive.Entries.Count > 10000) throw Bound();
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var files = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            long declared = 0;
            foreach (var entry in archive.Entries)
            {
                ct.ThrowIfCancellationRequested();
                string name = entry.FullName;
                bool directory = name.EndsWith('/');
                string normalized = directory ? name[..^1] : name;
                if (name.Length > 2048 || !names.Add(normalized) || string.IsNullOrEmpty(normalized)
                    || name.Any(c => char.IsControl(c) || c is '\\' or ':' || char.GetUnicodeCategory(c) == System.Globalization.UnicodeCategory.Format)
                    || normalized.Split('/').Any(part => part is "" or "." or ".." || part.EndsWith('.') || part.EndsWith(' '))
                    || ((entry.ExternalAttributes >> 16) & 0xf000) == 0xa000) throw Unsafe();
                if (!directory) files.Add(normalized);
                declared = checked(declared + entry.Length);
                if (declared > 256 * 1024 * 1024 || entry.Length > maximum) throw Bound();
            }
            foreach (string name in names)
            {
                int slash = name.IndexOf('/');
                while (slash >= 0)
                {
                    if (files.Contains(name[..slash])) throw Unsafe();
                    slash = name.IndexOf('/', slash + 1);
                }
            }
            if (manifest)
            {
                var manifests = archive.Entries.Where(entry => !entry.FullName.Contains('/') && entry.FullName.EndsWith(".nuspec", StringComparison.OrdinalIgnoreCase)).ToArray();
                if (manifests.Length != 1)
                    throw new AdoException("invalid_package_manifest", "Package inspection requires exactly one root nuspec file.", ExitCode.Safety);
                entryName = manifests[0].FullName;
            }
            var selected = entryName is null ? archive.Entries.ToArray() : archive.Entries.Where(entry => entry.FullName == entryName).ToArray();
            if (entryName is not null && selected.Length == 0)
                throw new AdoException("archive_entry_not_found", "The exact archive entry was not found.", ExitCode.NotFound);
            var results = new List<ArchiveEntryInfo>();
            bool textTruncated = false;
            foreach (var entry in selected.Take(limit))
            {
                ct.ThrowIfCancellationRequested();
                string? hash = null;
                List<string>? lines = null;
                int? lineCount = null;
                if (showText && (entry.FullName.EndsWith('/') || entry.Length > 1024 * 1024))
                    throw new AdoException("archive_text_limit", "Text inspection requires a file of at most 1 MiB expanded size.", ExitCode.Safety);
                if (entryName is not null && !entry.FullName.EndsWith('/'))
                {
                    FileStream? extraction = null;
                    if (target is not null)
                    {
                        target.Check();
                        string candidate = target.TemporaryPath();
                        extraction = DownloadTarget.CreatePrivateFile(candidate, 65536);
                        temporary = candidate;
                    }
                    await using var extractionOutput = extraction;
                    using var text = showText ? new MemoryStream() : null;
                    await using var content = entry.Open();
                    using var hasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                    var buffer = new byte[65536];
                    long read = 0;
                    int count;
                    while ((count = await content.ReadAsync(buffer, ct)) != 0)
                    {
                        read += count;
                        if (read > maximum || read > entry.Length) throw Bound();
                        hasher.AppendData(buffer, 0, count);
                        if (extractionOutput is not null) await extractionOutput.WriteAsync(buffer.AsMemory(0, count), ct);
                        if (text is not null)
                        {
                            if (read > 1024 * 1024) throw Bound();
                            await text.WriteAsync(buffer.AsMemory(0, count), ct);
                        }
                    }
                    if (read != entry.Length) throw new InvalidDataException();
                    if (extractionOutput is not null) await extractionOutput.FlushAsync(ct);
                    hash = Convert.ToHexStringLower(hasher.GetHashAndReset());
                    if (text is not null)
                    {
                        string decoded;
                        try { decoded = new System.Text.UTF8Encoding(false, true).GetString(text.ToArray()); }
                        catch (System.Text.DecoderFallbackException) { throw InvalidText(); }
                        if (decoded.Contains('\0')) throw InvalidText();
                        if (decoded.StartsWith('\uFEFF')) decoded = decoded[1..];
                        lines = [];
                        lineCount = 0;
                        using var reader = new StringReader(decoded);
                        while (reader.ReadLine() is { } line)
                        {
                            ct.ThrowIfCancellationRequested();
                            lineCount++;
                            if (lines.Count < textLines) lines.Add(line);
                        }
                        textTruncated = lineCount > textLines;
                    }
                }
                results.Add(new(entry.FullName, entry.FullName.EndsWith('/'), entry.Length, entry.CompressedLength, hash, lines, lineCount));
            }
            bool truncated = selected.Length > limit || textTruncated;
            if (target is not null)
            {
                ct.ThrowIfCancellationRequested();
                target.Check();
                File.Move(temporary!, target.Path, overwrite: false);
                temporary = null;
            }
            return new(new(snapshot.Length, digest, expectedHash is null ? null : true, archive.Entries.Count, results),
                new(Truncated: truncated, Completeness: truncated ? "partial" : "complete", TruncationReason: textTruncated ? "line_limit" : truncated ? "item_limit" : null, ScannedCount: archive.Entries.Count));
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        { throw new AdoException("archive_timeout", "Archive inspection exceeded its 60-second deadline.", ExitCode.Transient); }
        catch (FileNotFoundException) { throw new AdoException("archive_not_found", "The local archive file was not found.", ExitCode.NotFound); }
        catch (DirectoryNotFoundException) { throw new AdoException("archive_not_found", "The local archive file was not found.", ExitCode.NotFound); }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException or NotSupportedException or OverflowException)
        { throw new AdoException("invalid_archive", "The ZIP could not be processed or the selected output could not be written.", ExitCode.Safety); }
        finally
        {
            if (temporary is not null)
                try { File.Delete(temporary); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
    }
    private static AdoException Unsafe() => new("unsafe_archive", "The archive path or entry names contain unsafe links, paths or collisions.", ExitCode.Safety);
    private static AdoException InvalidText() => new("invalid_archive_text", "The selected member is not supported UTF-8 text (invalid encoding or NUL bytes). Use metadata/hash inspection for binary members.", ExitCode.Safety);
    private static AdoException Bound() => new("archive_limit", "Archive limits exceeded: 64 MiB file/member, 256 MiB declared expanded total, or 10000 entries.", ExitCode.Safety);
}
