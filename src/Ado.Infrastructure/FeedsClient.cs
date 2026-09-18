using System.Text.Json;
using Ado.Application;
using Ado.Domain;
using Ado.Infrastructure.Http;

namespace Ado.Infrastructure;

public sealed class FeedsClient(ServiceTransport transport, string organization, string? project)
{
    public async Task<CollectionResult<FeedInfo>> ReadAsync(string? feed, int limit, CancellationToken cancellationToken)
    {
        if (limit < 1) throw new AdoException("invalid_pagination", "Limit must be positive.", ExitCode.Usage);
        var operation = feed is null ? Operations.FeedList : Operations.FeedGet;
        using var response = await transport.GetAsync(operation, EndpointBuilder.Feed(operation, organization, project, feed), cancellationToken, project);
        if (response.ContinuationToken is not null) throw Invalid();
        var root = response.Document.RootElement;
        if (feed is not null)
        {
            var item = Parse(root);
            bool matches = Guid.TryParse(feed, out var id) ? item.Id == id : string.Equals(item.Name, feed, StringComparison.OrdinalIgnoreCase);
            if (!matches || (project is null && item.ProjectId is not null)) throw Invalid();
            return new([item], new(Organization: organization, Project: project, RequestId: response.RequestId));
        }
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("value", out var values) || values.ValueKind != JsonValueKind.Array
            || values.GetArrayLength() > 10000) throw Invalid();
        var items = new List<FeedInfo>();
        var seen = new HashSet<Guid>();
        foreach (var value in values.EnumerateArray())
        {
            var item = Parse(value);
            if (!seen.Add(item.Id)) throw Invalid();
            if (items.Count < limit) items.Add(item);
        }
        bool truncated = values.GetArrayLength() > limit;
        return new(items, new(Organization: organization, Project: project, RequestId: response.RequestId,
            Truncated: truncated, Completeness: truncated ? "partial" : "complete",
            TruncationReason: truncated ? "item_limit" : null, ScannedCount: values.GetArrayLength()));
    }

    private FeedInfo Parse(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Object || !Guid.TryParse(Text(value, "id"), out var id) || id == Guid.Empty) throw Invalid();
        string name = Text(value, "name") ?? throw Invalid();
        if (string.IsNullOrWhiteSpace(name)) throw Invalid();
        Guid? projectId = null;
        string? projectName = null;
        if (value.TryGetProperty("project", out var returnedProject) && returnedProject.ValueKind != JsonValueKind.Null)
        {
            if (returnedProject.ValueKind != JsonValueKind.Object || !Guid.TryParse(Text(returnedProject, "id"), out var pid) || pid == Guid.Empty) throw Invalid();
            projectId = pid;
            projectName = Text(returnedProject, "name");
        }
        if (project is not null && (projectId is null || (Guid.TryParse(project, out var requestedId)
            ? projectId != requestedId : projectName is not null && !string.Equals(projectName, project, StringComparison.OrdinalIgnoreCase)))) throw Invalid();
        return new(id, name, projectId is null ? "organization" : "project", projectId, projectName);
    }

    private string? Text(JsonElement value, string name)
    {
        if (!value.TryGetProperty(name, out var field) || field.ValueKind == JsonValueKind.Null) return null;
        if (field.ValueKind != JsonValueKind.String || field.GetString()!.Length > 1024) throw Invalid();
        return transport.Redact(field.GetString()!);
    }
    private static AdoException Invalid() => new("invalid_service_response", "The Feed response is malformed or violates its endpoint contract.", ExitCode.Transient);
}
