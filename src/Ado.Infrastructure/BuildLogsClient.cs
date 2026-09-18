using System.Text.Json;
using Ado.Application;
using Ado.Domain;
using Ado.Infrastructure.Http;

namespace Ado.Infrastructure;

public sealed class BuildLogsClient(ServiceTransport transport, string organization, string project)
{
    public async Task<CollectionResult<BuildLogInfo>> ListAsync(int buildId, int limit, CancellationToken cancellationToken)
    {
        if (limit < 1) throw Usage();
        using var response = await transport.GetAsync(Operations.BuildLogs,
            EndpointBuilder.BuildLog(Operations.BuildLogs, organization, project, buildId), cancellationToken, project);
        var values = Values(response.Document.RootElement);
        if (values.ValueKind != JsonValueKind.Array || response.ContinuationToken is not null) throw Invalid();
        var items = new List<BuildLogInfo>();
        var ids = new HashSet<int>();
        foreach (var value in values.EnumerateArray())
        {
            if (items.Count >= limit) break;
            if (value.ValueKind != JsonValueKind.Object || !value.TryGetProperty("id", out var id) || id.ValueKind != JsonValueKind.Number
                || !id.TryGetInt32(out int number) || number <= 0 || !ids.Add(number)) throw Invalid();
            long? count = null;
            if (value.TryGetProperty("lineCount", out var field) && field.ValueKind != JsonValueKind.Null)
            {
                if (field.ValueKind != JsonValueKind.Number || !field.TryGetInt64(out long total) || total < 0) throw Invalid();
                count = total;
            }
            string? type = null;
            if (value.TryGetProperty("type", out var typeValue) && typeValue.ValueKind != JsonValueKind.Null)
            {
                if (typeValue.ValueKind != JsonValueKind.String || typeValue.GetString()!.Length > 128) throw Invalid();
                type = transport.Redact(typeValue.GetString()!);
            }
            DateTimeOffset? Date(string name)
            {
                if (!value.TryGetProperty(name, out var date) || date.ValueKind == JsonValueKind.Null) return null;
                if (date.ValueKind != JsonValueKind.String || !date.TryGetDateTimeOffset(out var parsed)) throw Invalid();
                return parsed;
            }
            items.Add(new(number, buildId, count, type, Date("createdOn"), Date("lastChangedOn")));
        }
        bool truncated = values.GetArrayLength() > limit;
        return new(items, new(Organization: organization, Project: project, RequestId: response.RequestId,
            Truncated: truncated, Completeness: truncated ? "partial" : "complete", TruncationReason: truncated ? "item_limit" : null,
            ScannedCount: values.GetArrayLength()));
    }

    public async Task<BuildLogResult> GetAsync(int buildId, int logId, long? startLine, long? endLine, int limit, CancellationToken cancellationToken)
    {
        if (limit < 1) throw Usage();
        using var response = await transport.GetAsync(Operations.BuildLogGet,
            EndpointBuilder.BuildLog(Operations.BuildLogGet, organization, project, buildId, logId, startLine, endLine), cancellationToken, project);
        if (response.ContinuationToken is not null) throw Invalid();
        var value = Values(response.Document.RootElement);
        var lines = new List<string>();
        int count = 0;
        void Add(string line)
        {
            count++;
            if (lines.Count < limit) lines.Add(transport.Redact(line));
        }
        if (value.ValueKind == JsonValueKind.String)
        {
            // Redact the complete text first, including credentials that span line breaks.
            using var reader = new StringReader(transport.Redact(value.GetString()!));
            while (reader.ReadLine() is { } line) { cancellationToken.ThrowIfCancellationRequested(); Add(line); }
        }
        else if (value.ValueKind == JsonValueKind.Array)
        {
            foreach (var line in value.EnumerateArray())
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (line.ValueKind != JsonValueKind.String) throw Invalid();
                Add(line.GetString()!);
            }
        }
        else throw Invalid();
        bool truncated = count > limit;
        bool range = startLine is not null || endLine is not null;
        return new(new(buildId, logId, startLine, endLine, lines), new(Organization: organization, Project: project, RequestId: response.RequestId,
            Truncated: truncated, Completeness: truncated ? "partial" : range ? "unknown" : "complete",
            TruncationReason: truncated ? "line_limit" : range ? "requested_range" : null, ScannedCount: count));
    }

    private static JsonElement Values(JsonElement value) => value.ValueKind == JsonValueKind.Object && value.TryGetProperty("value", out var items) ? items : value;
    private static AdoException Invalid() => new("invalid_service_response", "The Build log response is malformed or violates its endpoint contract.", ExitCode.Transient);
    private static AdoException Usage() => new("invalid_limit", "The output limit must be positive.", ExitCode.Usage);
}
