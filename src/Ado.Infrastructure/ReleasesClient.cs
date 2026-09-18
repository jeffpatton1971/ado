using System.Text.Json;
using Ado.Application;
using Ado.Domain;
using Ado.Infrastructure.Http;

namespace Ado.Infrastructure;

public sealed class ReleasesClient(ServiceTransport transport, string organization, string project)
{
    public async Task<CollectionResult<ReleaseInfo>> ListAsync(int pageSize, int limit, string? continuation,
        int? definitionId, CancellationToken cancellationToken)
    {
        if (pageSize < 1 || limit < 1) throw new AdoException("invalid_pagination", "Page size and limit must be positive.", ExitCode.Usage);
        var items = new List<ReleaseInfo>();
        var ids = new HashSet<int>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        if (continuation is not null) seen.Add(continuation);
        string? next = continuation, requestId = null, reason = null;
        int bytes = 0;
        for (int page = 0; page < 100; page++)
        {
            int top = Math.Min(pageSize, limit - items.Count);
            using var response = await transport.GetAsync(Operations.ReleaseList,
                EndpointBuilder.Release(Operations.ReleaseList, organization, project, top: top, continuation: next, definitionId: definitionId), cancellationToken, project);
            var values = response.Document.RootElement;
            if (values.ValueKind == JsonValueKind.Object && values.TryGetProperty("value", out var array)) values = array;
            if (values.ValueKind != JsonValueKind.Array || values.GetArrayLength() > top) throw Invalid();
            foreach (var value in values.EnumerateArray())
            {
                var release = Parse(value);
                if (!ids.Add(release.Id)) throw Invalid();
                if (definitionId is { } id && release.DefinitionId != id) throw Invalid();
                items.Add(release);
            }
            bytes += response.Bytes;
            requestId = response.RequestId;
            next = response.ContinuationToken;
            if (next is null) break;
            try { _ = EndpointBuilder.Release(Operations.ReleaseList, organization, project, continuation: next); }
            catch (AdoException) { throw Invalid(); }
            if (!seen.Add(next)) throw Invalid();
            if (items.Count >= limit || page == 99 || bytes >= 64 * 1024 * 1024)
            {
                reason = items.Count >= limit ? "item_limit" : page == 99 ? "page_limit" : "byte_limit";
                break;
            }
        }
        return new(items, new(Organization: organization, Project: project, RequestId: requestId, ContinuationToken: next,
            Truncated: reason is not null, Completeness: reason is null ? "complete" : "partial", TruncationReason: reason, ScannedCount: items.Count));
    }

    public async Task<CollectionResult<ReleaseInfo>> GetAsync(int releaseId, CancellationToken cancellationToken)
    {
        using var response = await transport.GetAsync(Operations.ReleaseGet,
            EndpointBuilder.Release(Operations.ReleaseGet, organization, project, releaseId), cancellationToken, project);
        var release = Parse(response.Document.RootElement);
        if (release.Id != releaseId || response.ContinuationToken is not null) throw Invalid();
        return new([release], new(Organization: organization, Project: project, RequestId: response.RequestId));
    }

    private ReleaseInfo Parse(JsonElement value)
    {
        int id = PositiveId(value);
        if (!value.TryGetProperty("releaseDefinition", out var definition)) throw Invalid();
        int definitionId = PositiveId(definition);
        if (value.TryGetProperty("projectReference", out var returnedProject))
        {
            if (returnedProject.ValueKind != JsonValueKind.Object) throw Invalid();
            if (Guid.TryParse(project, out var expectedId))
            {
                if (!returnedProject.TryGetProperty("id", out var identity) || identity.ValueKind != JsonValueKind.String
                    || !identity.TryGetGuid(out var actualId) || actualId != expectedId) throw Invalid();
            }
            else if (returnedProject.TryGetProperty("name", out var name) && name.ValueKind != JsonValueKind.Null)
            {
                if (name.ValueKind != JsonValueKind.String || !string.Equals(name.GetString(), project, StringComparison.OrdinalIgnoreCase)) throw Invalid();
            }
            else
            {
                // Get Release documents an ID-only reference (name:null). The request
                // remains bound to the configured project route; there is no name to compare.
                if (!returnedProject.TryGetProperty("id", out var identity) || identity.ValueKind != JsonValueKind.String
                    || !identity.TryGetGuid(out var actualId) || actualId == Guid.Empty) throw Invalid();
            }
        }
        DateTimeOffset? Date(string name)
        {
            if (!value.TryGetProperty(name, out var field) || field.ValueKind == JsonValueKind.Null) return null;
            if (field.ValueKind != JsonValueKind.String || !field.TryGetDateTimeOffset(out var date)) throw Invalid();
            return date;
        }
        // Omit variables, environments, approvals, identities, artifacts, properties and URLs.
        return new(id, Text(value, "name", 1024), definitionId, Text(definition, "name", 1024),
            Text(value, "status", 64), Date("createdOn"), Date("modifiedOn"));
    }

    private string? Text(JsonElement value, string name, int maximum)
    {
        if (!value.TryGetProperty(name, out var field) || field.ValueKind == JsonValueKind.Null) return null;
        if (field.ValueKind != JsonValueKind.String || field.GetString()!.Length > maximum) throw Invalid();
        return transport.Redact(field.GetString()!);
    }
    private static int PositiveId(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Object || !value.TryGetProperty("id", out var id)
            || id.ValueKind != JsonValueKind.Number || !id.TryGetInt32(out int number) || number <= 0) throw Invalid();
        return number;
    }
    private static AdoException Invalid() => new("invalid_service_response", "The Release response is malformed or violates its endpoint contract.", ExitCode.Transient);
}
