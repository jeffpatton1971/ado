using System.Globalization;
using System.Text.Json;
using Ado.Application;
using Ado.Domain;
using Ado.Infrastructure.Http;

namespace Ado.Infrastructure;

public sealed class ProjectsClient(ServiceTransport transport, string organization)
{
    public async Task<ProjectResult> GetAsync(string project, CancellationToken cancellationToken)
    {
        using var response = await transport.GetAsync(Operations.ProjectGet, EndpointBuilder.Project(Operations.ProjectGet, organization, project), cancellationToken);
        return new([Parse(response.Document.RootElement)], new(Organization: organization, Project: project, RequestId: response.RequestId));
    }

    public async Task<ProjectResult> ListAsync(int pageSize, int limit, string? continuationToken, string? search, CancellationToken cancellationToken)
    {
        if (pageSize < 1 || limit < 1 || (continuationToken is not null && (!int.TryParse(continuationToken, NumberStyles.None, CultureInfo.InvariantCulture, out int offset) || offset < 0)))
            throw new AdoException("invalid_pagination", "Pagination sizes must be positive and project continuation must be a nonnegative integer.", ExitCode.Usage);
        var projects = new List<ProjectInfo>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        string? next = continuationToken, requestId = null;
        int scanned = 0, bytes = 0;
        bool truncated = false;
        string? reason = null;
        for (int page = 0; page < 100; page++)
        {
            int top = Math.Min(pageSize, limit - scanned);
            var uri = EndpointBuilder.Project(Operations.ProjectList, organization, query: new Dictionary<string, string?>
            {
                ["$top"] = top.ToString(CultureInfo.InvariantCulture), ["continuationToken"] = next
            });
            using var response = await transport.GetAsync(Operations.ProjectList, uri, cancellationToken);
            requestId = response.RequestId;
            bytes += response.Bytes;
            if (response.Document.RootElement.ValueKind != JsonValueKind.Object || !response.Document.RootElement.TryGetProperty("value", out var values) || values.ValueKind != JsonValueKind.Array || values.GetArrayLength() > top)
                throw InvalidResponse();
            foreach (var item in values.EnumerateArray())
            {
                var project = Parse(item);
                scanned++;
                if (search is null || project.Name.Contains(search, StringComparison.OrdinalIgnoreCase)) projects.Add(project);
            }
            next = response.ContinuationToken;
            if (next is null) break;
            if (!seen.Add(next) || next == continuationToken) throw InvalidResponse();
            if (scanned >= limit || page == 99 || bytes >= 64 * 1024 * 1024)
            {
                truncated = true;
                reason = scanned >= limit ? "item_limit" : page == 99 ? "page_limit" : "byte_limit";
                break;
            }
        }
        return new(projects, new(Organization: organization, RequestId: requestId, ContinuationToken: next,
            Truncated: truncated, Completeness: truncated ? "partial" : "complete", TruncationReason: reason, ScannedCount: scanned));
    }

    private ProjectInfo Parse(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty("id", out var id) || id.ValueKind != JsonValueKind.String
            || !Guid.TryParse(id.GetString(), out Guid parsedId)) throw InvalidResponse();
        string? Field(string name, int max)
        {
            if (!element.TryGetProperty(name, out var field) || field.ValueKind == JsonValueKind.Null) return null;
            if (field.ValueKind != JsonValueKind.String || field.GetString()!.Length > max) throw InvalidResponse();
            return transport.Redact(field.GetString()!);
        }
        return new(parsedId, Field("name", 1024) ?? throw InvalidResponse(), Field("description", 16384), Field("state", 64), Field("visibility", 64));
    }
    private static AdoException InvalidResponse() => new("invalid_service_response", "The project response is malformed or violates its pagination contract.", ExitCode.Transient);
}
