using System.Text.Json;
using Ado.Application;
using Ado.Domain;
using Ado.Infrastructure.Http;

namespace Ado.Infrastructure;

public sealed class BuildTimelineClient(ServiceTransport transport, string organization, string project)
{
    public async Task<CollectionResult<BuildTimelineRecord>> GetAsync(int buildId, int limit, CancellationToken cancellationToken)
    {
        if (limit < 1) throw new AdoException("invalid_limit", "The timeline limit must be positive.", ExitCode.Usage);
        var buildUri = EndpointBuilder.Build(Operations.BuildGet, organization, project, buildId);
        var uri = new UriBuilder(buildUri) { Path = buildUri.AbsolutePath + "/timeline" }.Uri;
        using var response = await transport.GetAsync(Operations.BuildTimeline, uri, cancellationToken, project);
        var root = response.Document.RootElement;
        if (response.ContinuationToken is not null || root.ValueKind != JsonValueKind.Object
            || !root.TryGetProperty("records", out var records) || records.ValueKind != JsonValueKind.Array) throw Invalid();
        var items = new List<BuildTimelineRecord>();
        var ids = new HashSet<Guid>();
        bool hasDetails = false;
        foreach (var value in records.EnumerateArray())
        {
            if (items.Count >= limit) break;
            if (value.ValueKind != JsonValueKind.Object || !value.TryGetProperty("id", out var id)
                || id.ValueKind != JsonValueKind.String || !id.TryGetGuid(out var recordId) || !ids.Add(recordId)) throw Invalid();
            Guid? parentId = null;
            if (value.TryGetProperty("parentId", out var parent) && parent.ValueKind != JsonValueKind.Null)
            {
                if (parent.ValueKind != JsonValueKind.String || !parent.TryGetGuid(out var parsed)) throw Invalid();
                parentId = parsed;
            }
            int? logId = null;
            if (value.TryGetProperty("log", out var log) && log.ValueKind != JsonValueKind.Null)
            {
                if (log.ValueKind != JsonValueKind.Object) throw Invalid();
                logId = Number(log, "id");
                if (logId is null or <= 0) throw Invalid();
            }
            hasDetails |= value.TryGetProperty("details", out var details) && details.ValueKind != JsonValueKind.Null;
            items.Add(new(recordId, parentId, buildId, Text(value, "name", 2048), Text(value, "type", 128),
                Text(value, "state", 64), Text(value, "result", 64), logId, Number(value, "order"),
                Number(value, "errorCount"), Number(value, "warningCount")));
        }
        bool truncated = records.GetArrayLength() > limit;
        string? reason = truncated ? "item_limit" : hasDetails ? "sub_timelines_not_loaded" : null;
        return new(items, new(Organization: organization, Project: project, RequestId: response.RequestId,
            Truncated: truncated, Completeness: truncated ? "partial" : hasDetails ? "unknown" : "complete",
            TruncationReason: reason, ScannedCount: records.GetArrayLength()));
    }

    private string? Text(JsonElement value, string name, int maximum)
    {
        if (!value.TryGetProperty(name, out var field) || field.ValueKind == JsonValueKind.Null) return null;
        if (field.ValueKind != JsonValueKind.String || field.GetString()!.Length > maximum) throw Invalid();
        return transport.Redact(field.GetString()!);
    }

    private static int? Number(JsonElement value, string name)
    {
        if (!value.TryGetProperty(name, out var field) || field.ValueKind == JsonValueKind.Null) return null;
        if (field.ValueKind != JsonValueKind.Number || !field.TryGetInt32(out int number) || number < 0) throw Invalid();
        return number;
    }

    private static AdoException Invalid() => new("invalid_service_response", "The build timeline response is malformed.", ExitCode.Transient);
}
