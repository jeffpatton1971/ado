using Ado.Application;
using Ado.Domain;
using Ado.Infrastructure.Http;

namespace Ado.Infrastructure;

public sealed record PackageDownloadResult(string Destination, long Bytes, string Sha256, PackageResolution Resolution,
    string ContentVerification, IReadOnlyList<string> Limitations);

public sealed class NuGetPackageDownloader(PackagesClient packages, BuildArtifactDownloader downloads, string organization, string? project, string feed)
{
    public async Task<PackageDownloadResult> DownloadAsync(PackageQuery query, int pageSize, int limit, DownloadTarget target,
        long maxBytes, int timeoutSeconds, CancellationToken cancellationToken)
    {
        target.Check();
        var resolved = (await packages.ResolveAsync(query, pageSize, limit, cancellationToken)).Items[0];
        var uri = EndpointBuilder.NuGetContent(organization, project, feed, resolved.Package.Name,
            resolved.Version.NormalizedVersion ?? resolved.Version.Version);
        var result = await downloads.DownloadZipAsync(uri, ServiceHost.Packages, target, maxBytes, timeoutSeconds, cancellationToken);
        return new(target.Path, result.Bytes, result.Sha256, resolved, "zip_envelope_only",
            ["Metadata resolution and download are separate reads, not a transactional snapshot.",
             "SHA-256 identifies downloaded bytes; it does not verify publisher authenticity or reproducible provenance.",
             "Only the ZIP envelope was checked. NuGet identity, signatures, archive members and package semantics were not verified. No contents were extracted or executed."]);
    }
}
