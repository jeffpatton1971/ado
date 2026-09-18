using System.Globalization;
using System.Text.Json;
using Ado.Application;
using Ado.Domain;
using Ado.Infrastructure.Http;

namespace Ado.Infrastructure;

public sealed class PackagesClient(ServiceTransport transport, string organization, string? project, string feed)
{
    public async Task<CollectionResult<PackageInfo>> ListAsync(PackageQuery query, int pageSize, int limit, string? continuation, CancellationToken cancellationToken)
    {
        query.Validate("package list");
        int offset = PackageQuery.Offset(continuation);
        if (pageSize < 1 || limit < 1) throw new AdoException("invalid_pagination", "Package bounds must be positive.", ExitCode.Usage);
        var items = new List<PackageInfo>();
        var seen = new HashSet<Guid>();
        int bytes = 0;
        string? requestId = null, reason = null;
        for (int page = 0; page < 100; page++)
        {
            int top = Math.Min(pageSize, limit - items.Count);
            using var response = await transport.GetAsync(Operations.PackageList,
                EndpointBuilder.Package(Operations.PackageList, organization, project, feed, query, top, offset), cancellationToken, project);
            if (response.ContinuationToken is not null) throw Invalid();
            var values = Values(response.Document.RootElement, top);
            requestId = response.RequestId;
            if (values.GetArrayLength() == 0) return new(items, new(Organization: organization, Project: project, RequestId: requestId, ScannedCount: items.Count));
            foreach (var value in values.EnumerateArray())
            {
                var item = new PackageInfo(Id(value), Required(value, "name"), Text(value, "normalizedName"), Required(value, "protocolType"));
                if (!seen.Add(item.Id) || (query.Protocol is not null && !string.Equals(query.Protocol, item.ProtocolType, StringComparison.OrdinalIgnoreCase))) throw Invalid();
                items.Add(item);
            }
            if (offset > int.MaxValue - values.GetArrayLength()) throw Invalid();
            offset += values.GetArrayLength();
            bytes += response.Bytes;
            if (items.Count >= limit || page == 99 || bytes > 60 * 1024 * 1024)
            {
                reason = items.Count >= limit ? "item_limit" : page == 99 ? "page_limit" : "byte_limit";
                break;
            }
        }
        return new(items, new(Organization: organization, Project: project, RequestId: requestId,
            ContinuationToken: offset.ToString(CultureInfo.InvariantCulture), Truncated: true, Completeness: "partial", TruncationReason: reason, ScannedCount: items.Count));
    }

    public async Task<CollectionResult<PackageVersionInfo>> VersionsAsync(PackageQuery query, int limit, bool get, CancellationToken cancellationToken)
    {
        var operation = get ? Operations.PackageVersionGet : Operations.PackageVersions;
        if (limit < 1) throw new AdoException("invalid_pagination", "Limit must be positive.", ExitCode.Usage);
        using var response = await transport.GetAsync(operation, EndpointBuilder.Package(operation, organization, project, feed, query), cancellationToken, project);
        if (response.ContinuationToken is not null) throw Invalid();
        var root = response.Document.RootElement;
        Guid packageId = Guid.Parse(query.PackageId!);
        if (get)
        {
            var item = Version(root, packageId);
            if (item.Id != Guid.Parse(query.VersionId!)) throw Invalid();
            return new([item], new(Organization: organization, Project: project, RequestId: response.RequestId));
        }
        var values = Values(root, 10000);
        var items = new List<PackageVersionInfo>();
        var seen = new HashSet<Guid>();
        foreach (var value in values.EnumerateArray())
        {
            var item = Version(value, packageId);
            if (!seen.Add(item.Id) || item.IsDeleted == true) throw Invalid();
            if (items.Count < limit) items.Add(item);
        }
        bool partial = values.GetArrayLength() > limit;
        return new(items, new(Organization: organization, Project: project, RequestId: response.RequestId,
            Truncated: partial, Completeness: partial ? "partial" : "complete", TruncationReason: partial ? "item_limit" : null, ScannedCount: values.GetArrayLength()));
    }

    private PackageVersionInfo Version(JsonElement value, Guid packageId)
    {
        Guid id = Id(value);
        bool? Flag(string name)
        {
            if (!value.TryGetProperty(name, out var field) || field.ValueKind == JsonValueKind.Null) return null;
            if (field.ValueKind is not (JsonValueKind.True or JsonValueKind.False)) throw Invalid();
            return field.GetBoolean();
        }
        DateTimeOffset? published = null;
        if (value.TryGetProperty("publishDate", out var date) && date.ValueKind != JsonValueKind.Null)
        {
            if (date.ValueKind != JsonValueKind.String || !date.TryGetDateTimeOffset(out var parsed)) throw Invalid();
            published = parsed;
        }
        return new(id, packageId, Required(value, "version"), Text(value, "normalizedVersion"), Flag("isListed"), Flag("isDeleted"), Flag("isLatest"), published);
    }
    private static JsonElement Values(JsonElement value, int maximum)
    {
        if (value.ValueKind == JsonValueKind.Object && value.TryGetProperty("value", out var array)) value = array;
        if (value.ValueKind != JsonValueKind.Array || value.GetArrayLength() > maximum) throw Invalid();
        return value;
    }
    private static Guid Id(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Object || !value.TryGetProperty("id", out var field) || field.ValueKind != JsonValueKind.String
            || !Guid.TryParse(field.GetString(), out var id) || id == Guid.Empty) throw Invalid();
        return id;
    }
    private string Required(JsonElement value, string name) => Text(value, name) is { } text && !string.IsNullOrWhiteSpace(text) ? text : throw Invalid();
    private string? Text(JsonElement value, string name)
    {
        if (!value.TryGetProperty(name, out var field) || field.ValueKind == JsonValueKind.Null) return null;
        if (field.ValueKind != JsonValueKind.String || field.GetString()!.Length > 1024) throw Invalid();
        return transport.Redact(field.GetString()!);
    }
    private static AdoException Invalid() => new("invalid_service_response", "The Package response is malformed or violates its endpoint contract.", ExitCode.Transient);
}
