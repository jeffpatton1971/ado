using System.Text.Json;
using Ado.Application;
using Ado.Domain;
using Ado.Infrastructure.Http;

namespace Ado.Infrastructure;

public sealed class BuildsClient(ServiceTransport transport, string organization, string project)
{
    public async Task<CollectionResult<BuildInfo>> ListAsync(int pageSize, int limit, string? continuation,
        BuildFilters filters, CancellationToken cancellationToken)
    {
        filters.Validate();
        if (pageSize < 1 || limit < 1) throw new AdoException("invalid_pagination", "Page size and limit must be positive.", ExitCode.Usage);
        var items = new List<BuildInfo>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        if (continuation is not null) seen.Add(continuation);
        string? next = continuation, requestId = null, reason = null;
        int bytes = 0;
        for (int page = 0; page < 100; page++)
        {
            int top = Math.Min(pageSize, limit - items.Count);
            using var response = await transport.GetAsync(Operations.BuildList,
                EndpointBuilder.Build(Operations.BuildList, organization, project, top: top, continuation: next, filters: filters), cancellationToken, project);
            var values = response.Document.RootElement;
            if (values.ValueKind == JsonValueKind.Object && values.TryGetProperty("value", out var array)) values = array;
            if (values.ValueKind != JsonValueKind.Array || values.GetArrayLength() > top) throw Invalid();
            foreach (var value in values.EnumerateArray())
            {
                var build = Parse(value);
                if (filters.DefinitionId is { } id && build.DefinitionId != id) throw Invalid();
                items.Add(build);
            }
            bytes += response.Bytes;
            requestId = response.RequestId;
            next = response.ContinuationToken;
            if (next is null) break;
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

    public async Task<CollectionResult<BuildInfo>> GetAsync(int buildId, CancellationToken cancellationToken)
    {
        using var response = await transport.GetAsync(Operations.BuildGet,
            EndpointBuilder.Build(Operations.BuildGet, organization, project, buildId), cancellationToken, project);
        var build = Parse(response.Document.RootElement);
        if (build.Id != buildId) throw Invalid();
        return new([build], new(Organization: organization, Project: project, RequestId: response.RequestId));
    }

    private BuildInfo Parse(JsonElement value)
    {
        int id = PositiveId(value);
        if (!value.TryGetProperty("definition", out var definition)) throw Invalid();
        int definitionId = PositiveId(definition);
        if (value.TryGetProperty("project", out var returnedProject))
        {
            if (returnedProject.ValueKind != JsonValueKind.Object) throw Invalid();
            string field = Guid.TryParse(project, out _) ? "id" : "name";
            if (!returnedProject.TryGetProperty(field, out var identity) || identity.ValueKind != JsonValueKind.String
                || !string.Equals(identity.GetString(), project, StringComparison.OrdinalIgnoreCase)) throw Invalid();
        }
        DateTimeOffset? Date(string name)
        {
            if (!value.TryGetProperty(name, out var field) || field.ValueKind == JsonValueKind.Null) return null;
            if (field.ValueKind != JsonValueKind.String || !field.TryGetDateTimeOffset(out var date)) throw Invalid();
            return date;
        }
        // Omit identities, parameters, properties, repository payloads, logs/URLs and trigger metadata.
        return new(id, Text(value, "buildNumber", 1024), definitionId, Text(definition, "name", 1024),
            Text(value, "status", 64), Text(value, "result", 64), Text(value, "reason", 64),
            Text(value, "sourceBranch", 2048), Text(value, "sourceVersion", 1024), Date("queueTime"), Date("startTime"), Date("finishTime"));
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
    private static AdoException Invalid() => new("invalid_service_response", "The Build response is malformed or violates its endpoint contract.", ExitCode.Transient);
}
