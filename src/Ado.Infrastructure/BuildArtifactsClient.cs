using System.Text.Json;
using Ado.Application;
using Ado.Domain;
using Ado.Infrastructure.Http;

namespace Ado.Infrastructure;

public sealed class BuildArtifactsClient(ServiceTransport transport, string organization, string project)
{
    public async Task<CollectionResult<BuildOutputArtifactInfo>> ListAsync(int buildId, int limit, CancellationToken cancellationToken)
    {
        if (limit < 1) throw new AdoException("invalid_limit", "The output limit must be positive.", ExitCode.Usage);
        using var response = await transport.GetAsync(Operations.BuildArtifactList,
            EndpointBuilder.BuildArtifact(Operations.BuildArtifactList, organization, project, buildId), cancellationToken, project);
        var values = response.Document.RootElement;
        if (values.ValueKind == JsonValueKind.Object && values.TryGetProperty("value", out var array)) values = array;
        if (values.ValueKind != JsonValueKind.Array || response.ContinuationToken is not null) throw Invalid();
        var items = new List<BuildOutputArtifactInfo>();
        var ids = new HashSet<int>();
        foreach (var value in values.EnumerateArray())
        {
            if (items.Count >= limit) break;
            var item = Parse(value, buildId);
            if (!ids.Add(item.Id)) throw Invalid();
            items.Add(item);
        }
        bool truncated = values.GetArrayLength() > limit;
        return new(items, new(Organization: organization, Project: project, RequestId: response.RequestId,
            Truncated: truncated, Completeness: truncated ? "partial" : "complete", TruncationReason: truncated ? "item_limit" : null,
            ScannedCount: values.GetArrayLength()));
    }

    public async Task<CollectionResult<BuildOutputArtifactInfo>> GetAsync(int buildId, string artifactName, CancellationToken cancellationToken)
    {
        using var response = await transport.GetAsync(Operations.BuildArtifactGet,
            EndpointBuilder.BuildArtifact(Operations.BuildArtifactGet, organization, project, buildId, artifactName), cancellationToken, project);
        var value = response.Document.RootElement;
        if (response.ContinuationToken is not null || value.ValueKind != JsonValueKind.Object
            || Text(value, "name", 1024) != artifactName) throw Invalid();
        return new([Parse(value, buildId)], new(Organization: organization, Project: project, RequestId: response.RequestId));
    }

    public async Task<Uri> GetSignedContentAsync(int buildId, string artifactName, CancellationToken cancellationToken)
    {
        var build = await new BuildsClient(transport, organization, project).GetAsync(buildId, cancellationToken);
        using var response = await transport.GetAsync(Operations.PipelineArtifactSignedContent,
            EndpointBuilder.PipelineArtifact(organization, project, build.Items[0].DefinitionId, buildId, artifactName), cancellationToken, project);
        var value = response.Document.RootElement;
        if (response.ContinuationToken is not null || value.ValueKind != JsonValueKind.Object
            || Text(value, "name", 1024) != artifactName
            || !value.TryGetProperty("signedContent", out var signed) || signed.ValueKind != JsonValueKind.Object
            || !signed.TryGetProperty("signatureExpires", out var expiry) || expiry.ValueKind != JsonValueKind.String
            || !expiry.TryGetDateTimeOffset(out var expires) || expires <= DateTimeOffset.UtcNow
            || !Uri.TryCreate(Text(signed, "url", 16384), UriKind.Absolute, out var uri))
            throw new AdoException("invalid_signed_content", "The service did not return matching, unexpired signed artifact content.", ExitCode.Transient);
        // Keep the capability URL private to the download path; never include it in output.
        BuildArtifactDownloader.ValidateStorageDestination(uri);
        return uri;
    }

    private BuildOutputArtifactInfo Parse(JsonElement value, int buildId)
    {
        if (value.ValueKind != JsonValueKind.Object || !value.TryGetProperty("id", out var id) || id.ValueKind != JsonValueKind.Number
            || !id.TryGetInt32(out int number) || number <= 0) throw Invalid();
        string? name = Text(value, "name", 1024);
        if (string.IsNullOrWhiteSpace(name) || !value.TryGetProperty("resource", out var resource) || resource.ValueKind != JsonValueKind.Object) throw Invalid();
        // Resource data/properties and URLs can contain paths, credentials or signed download links.
        // Discovery never exposes or follows them, and does not assume a downloadable resource type.
        return new(number, buildId, transport.Redact(name), transport.Redact(Text(resource, "type", 128) ?? "unknown"),
            Text(value, "source", 1024) is { } source ? transport.Redact(source) : null);
    }

    private static string? Text(JsonElement value, string name, int maximum)
    {
        if (!value.TryGetProperty(name, out var field) || field.ValueKind == JsonValueKind.Null) return null;
        if (field.ValueKind != JsonValueKind.String || field.GetString()!.Length > maximum) throw Invalid();
        return field.GetString();
    }
    private static AdoException Invalid() => new("invalid_service_response", "The build-output response is malformed or does not match the requested artifact.", ExitCode.Transient);
}
