using System.Text.Json;
using Ado.Application;
using Ado.Domain;
using Ado.Infrastructure.Http;

namespace Ado.Infrastructure;

public sealed record AuthenticatedArtifactEvidence(int SchemaVersion, string Kind, string Organization, string Project,
    BuildInfo Build, BuildOutputArtifactInfo Artifact, EvidenceArchive Archive, EvidenceMember Member,
    string OriginVerification, string Completeness, IReadOnlyList<string> Limitations);
public sealed record BuildEvidenceExport(string Destination, bool Written, AuthenticatedArtifactEvidence Evidence);

public sealed class BuildArtifactEvidence(ServiceTransport transport, BuildArtifactDownloader downloader, string organization, string project)
{
    public async Task<BuildEvidenceExport> ExportAsync(int buildId, string artifactName, string entry, DownloadTarget target,
        long maxBytes, int downloadSeconds, CancellationToken cancellationToken)
    {
        var build = (await new BuildsClient(transport, organization, project).GetAsync(buildId, cancellationToken)).Items.Single();
        var artifacts = new BuildArtifactsClient(transport, organization, project);
        var artifact = (await artifacts.GetAsync(buildId, artifactName, cancellationToken)).Items.Single();
        if (artifact.ResourceType is not ("Container" or "PipelineArtifact"))
            throw new AdoException("unsupported_artifact_type", "Evidence download supports Container and PipelineArtifact ZIP outputs.", ExitCode.Safety);
        Uri? signed = artifact.ResourceType == "PipelineArtifact" ? await artifacts.GetSignedContentAsync(buildId, artifactName, cancellationToken) : null;
        string archivePath = target.TemporaryPath() + ".zip";
        bool archiveCreated = false;
        string? temporary = null;
        try
        {
            await downloader.DownloadAsync(buildId, artifactName, DownloadTarget.Validate(archivePath), Math.Min(maxBytes, 64 * 1024 * 1024), downloadSeconds, cancellationToken, signed);
            archiveCreated = true;
            var inspected = await ArtifactArchiveInspector.InspectAsync(archivePath, entry, null, 1, cancellationToken);
            var member = inspected.Data.Entries.Single();
            var evidence = new AuthenticatedArtifactEvidence(1, "azure_devops_build_artifact_member", transport.Redact(organization), transport.Redact(project), build, artifact,
                new(inspected.Data.ArchiveBytes, inspected.Data.Sha256, inspected.Data.TotalEntries, null),
                new(transport.Redact(member.Path), member.Bytes, member.CompressedBytes, member.Sha256!),
                "authenticated_metadata_and_download", "complete_for_selected_member",
                ["Build/artifact metadata was read with the selected credential and content downloaded from the scoped service or its validated signed storage route.",
                 "These separate reads are not a transactional snapshot. Public access may succeed without proving credential validity.",
                 "Hashes identify the bytes downloaded during this operation, not publisher authenticity or reproducible build provenance.",
                 "SourceVersion is the reported build source revision; shared-template revisions and package semantics were not verified.",
                 "Only the selected member was hashed. Raw contents, logs, signed URLs and local input paths are omitted; names may still be sensitive."]);
            // Do not leave downloaded content behind after a successful evidence export.
            File.Delete(archivePath);
            archiveCreated = false;
            target.Check();
            string candidate = target.TemporaryPath();
            await using (var output = new FileStream(candidate, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.Asynchronous))
            {
                temporary = candidate;
                await JsonSerializer.SerializeAsync(output, evidence, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true }, cancellationToken);
                await output.FlushAsync(cancellationToken);
            }
            cancellationToken.ThrowIfCancellationRequested();
            target.Check();
            File.Move(temporary, target.Path, overwrite: false);
            temporary = null;
            return new(transport.Redact(target.Path), true, evidence);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { throw new AdoException("evidence_write_failed", "The evidence operation could not publish its output or remove temporary content.", ExitCode.Safety); }
        finally
        {
            if (archiveCreated) try { File.Delete(archivePath); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            if (temporary is not null) try { File.Delete(temporary); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
    }
}
