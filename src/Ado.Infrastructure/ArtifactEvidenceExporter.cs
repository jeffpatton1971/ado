using System.Text.Json;
using Ado.Domain;
using Ado.Infrastructure.Http;

namespace Ado.Infrastructure;

public sealed record EvidenceOrigin(string? Organization, string? Project, int? BuildId, string? ArtifactName);
public sealed record EvidenceExport(string Destination, bool DryRun, bool Written, ArtifactEvidence Evidence);
public sealed record EvidenceArchive(long Bytes, string Sha256, int TotalEntries, bool? ExpectedHashMatches);
public sealed record EvidenceMember(string Path, long Bytes, long CompressedBytes, string Sha256);
public sealed record ArtifactEvidence(int SchemaVersion, string Kind, EvidenceArchive Archive, EvidenceMember Member,
    EvidenceOrigin? ClaimedOrigin, string OriginVerification, string Completeness, IReadOnlyList<string> Limitations);

public static class ArtifactEvidenceExporter
{
    public static async Task<EvidenceExport> ExportAsync(string? file, string? entry, string? destination, string? expectedHash,
        EvidenceOrigin? origin, bool dryRun, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrEmpty(entry) || entry.EndsWith('/'))
            throw new AdoException("archive_entry_required", "Evidence export requires one exact file --entry.", ExitCode.Usage);
        if (origin is not null)
        {
            EndpointBuilder.ValidateOrganization(origin.Organization);
            EndpointBuilder.ProjectSegment(origin.Project);
            if (origin.BuildId is null or <= 0 || string.IsNullOrWhiteSpace(origin.ArtifactName) || origin.ArtifactName.Length > 1024
                || origin.Project!.Length > 1024 || origin.ArtifactName.Any(char.IsControl))
                throw new AdoException("invalid_evidence_origin", "Supply organization, project, positive build ID and artifact name together for an optional unverified origin label.", ExitCode.Usage);
        }
        var target = DownloadTarget.Validate(destination);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(60));
        string? temporary = null;
        try
        {
            var inspected = await ArtifactArchiveInspector.InspectAsync(file, entry, expectedHash, 1, deadline.Token);
            var member = inspected.Data.Entries.Single();
            var evidence = new ArtifactEvidence(1, "local_archive_member",
                new(inspected.Data.ArchiveBytes, inspected.Data.Sha256, inspected.Data.TotalEntries, inspected.Data.ExpectedHashMatches),
                new(member.Path, member.Bytes, member.CompressedBytes, member.Sha256!), origin,
                origin is null ? "not_supplied" : "user_supplied_unverified", "complete_for_selected_member",
                ["Hashes identify locally inspected bytes; they do not establish publisher authenticity.",
                 "No Azure DevOps requests were made. Run, artifact and source provenance have not been verified.",
                 "Only the selected member was hashed; other members and package semantics were not verified.",
                 "Member contents, raw logs, credentials and absolute input paths are omitted. Selected names and optional origin labels may still be sensitive."]);
            if (dryRun) return new(target.Path, true, false, evidence);
            target.Check();
            string candidate = target.TemporaryPath();
            await using (var output = DownloadTarget.CreatePrivateFile(candidate))
            {
                temporary = candidate;
                await JsonSerializer.SerializeAsync(output, evidence, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true }, deadline.Token);
                await output.FlushAsync(deadline.Token);
            }
            deadline.Token.ThrowIfCancellationRequested();
            target.Check();
            File.Move(temporary, target.Path, overwrite: false);
            temporary = null;
            return new(target.Path, false, true, evidence);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        { throw new AdoException("evidence_timeout", "Evidence export exceeded its 60-second deadline.", ExitCode.Transient); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { throw new AdoException("evidence_write_failed", "The evidence file could not be published. Check the destination; overwrite is not supported.", ExitCode.Safety); }
        finally
        {
            if (temporary is not null)
                try { File.Delete(temporary); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
    }
}
