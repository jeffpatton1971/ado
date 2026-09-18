using System.Text.Json;
using Ado.Application;
using Ado.Domain;
using Ado.Infrastructure.Http;

namespace Ado.Infrastructure;

public sealed class PipelinesClient(ServiceTransport transport, string organization, string project)
{
    public async Task<PipelinePreviewInfo> PreviewAsync(int pipelineId, PipelineRunRequest request, string? confirmation,
        bool showYaml, CancellationToken cancellationToken)
    {
        using var response = await transport.PreviewPipelineAsync(project, pipelineId, request, confirmation, cancellationToken);
        var value = response.Document.RootElement;
        if (value.ValueKind != JsonValueKind.Object || !value.TryGetProperty("finalYaml", out var yaml)
            || yaml.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(yaml.GetString())) throw Invalid();
        if (value.TryGetProperty("pipeline", out var pipeline) && PositiveId(pipeline) != pipelineId) throw Invalid();
        string text = yaml.GetString()!;
        return new(pipelineId, true, text.Length, showYaml ? request.RedactInputValues(transport.Redact(text), cancellationToken) : null);
    }

    public async Task<int> StartAsync(int pipelineId, PipelineRunRequest request, string? confirmation, CancellationToken cancellationToken)
    {
        using var response = await transport.StartPipelineAsync(project, pipelineId, request.Serialize(), confirmation, cancellationToken);
        try
        {
            var value = response.Document.RootElement;
            int id = PositiveId(value);
            if (!value.TryGetProperty("pipeline", out var pipeline) || PositiveId(pipeline) != pipelineId) throw Invalid();
            // Only numeric identity is returned: the service may echo sensitive input in other fields.
            return id;
        }
        catch (AdoException) { throw ServiceTransport.UncertainWrite(); }
    }

    public async Task<CollectionResult<PipelineInfo>> ListAsync(int pageSize, int limit, string? continuation, CancellationToken cancellationToken)
    {
        if (pageSize < 1 || limit < 1) throw new AdoException("invalid_pagination", "Page size and limit must be positive.", ExitCode.Usage);
        var items = new List<PipelineInfo>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        if (continuation is not null) seen.Add(continuation);
        string? next = continuation, requestId = null, reason = null;
        int bytes = 0;
        for (int page = 0; page < 100; page++)
        {
            int top = Math.Min(pageSize, limit - items.Count);
            using var response = await transport.GetAsync(Operations.PipelineList,
                EndpointBuilder.Pipeline(Operations.PipelineList, organization, project, top: top, continuation: next), cancellationToken, project);
            var values = Array(response.Document.RootElement);
            if (values.GetArrayLength() > top) throw Invalid();
            foreach (var value in values.EnumerateArray()) items.Add(ParsePipeline(value));
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
        return new(items, Meta(requestId, next, reason, items.Count));
    }

    public async Task<CollectionResult<PipelineInfo>> GetAsync(int pipelineId, CancellationToken cancellationToken)
    {
        using var response = await transport.GetAsync(Operations.PipelineGet,
            EndpointBuilder.Pipeline(Operations.PipelineGet, organization, project, pipelineId), cancellationToken, project);
        var pipeline = ParsePipeline(response.Document.RootElement);
        if (pipeline.Id != pipelineId) throw Invalid();
        return new([pipeline], Meta(response.RequestId));
    }

    public async Task<CollectionResult<PipelineRunInfo>> RunsAsync(int pipelineId, int limit, CancellationToken cancellationToken)
    {
        if (limit < 1) throw new AdoException("invalid_pagination", "Limit must be positive.", ExitCode.Usage);
        using var response = await transport.GetAsync(Operations.PipelineRuns,
            EndpointBuilder.Pipeline(Operations.PipelineRuns, organization, project, pipelineId), cancellationToken, project);
        var values = Array(response.Document.RootElement);
        int count = values.GetArrayLength();
        if (count > 10000 || response.ContinuationToken is not null) throw Invalid();
        var runs = new List<PipelineRunInfo>();
        foreach (var value in values.EnumerateArray())
        {
            if (runs.Count >= limit) break;
            runs.Add(ParseRun(value, pipelineId));
        }
        // At the documented server ceiling the existence of older runs cannot be established.
        string? reason = count >= 10000 ? "server_limit" : count > limit ? "item_limit" : null;
        var meta = Meta(response.RequestId, reason: reason, scanned: count);
        if (count >= 10000) meta = meta with { Completeness = "unknown" };
        return new(runs, meta);
    }

    public async Task<CollectionResult<PipelineRunInfo>> RunGetAsync(int pipelineId, int runId, CancellationToken cancellationToken)
    {
        using var response = await transport.GetAsync(Operations.PipelineRunGet,
            EndpointBuilder.Pipeline(Operations.PipelineRunGet, organization, project, pipelineId, runId), cancellationToken, project);
        var run = ParseRun(response.Document.RootElement, pipelineId);
        if (run.Id != runId) throw Invalid();
        run = run with { RepositoryProvenance = ParseRepositoryProvenance(response.Document.RootElement) };
        return new([run], Meta(response.RequestId));
    }

    private RunRepositoryProvenance ParseRepositoryProvenance(JsonElement value)
    {
        string[] limitations = [
            "Repository versions are reported by this run; current branches and definitions were not queried.",
            "Aliases identify run resources, not globally unique repositories. Versions are service-reported and were not independently verified.",
            "This does not establish every template revision or checkout used by the run, or map PR head commits to merge commits."
        ];
        if (!value.TryGetProperty("resources", out var resources) || resources.ValueKind == JsonValueKind.Null)
            return new("unavailable", null, limitations);
        if (resources.ValueKind != JsonValueKind.Object) throw Invalid();
        if (!resources.TryGetProperty("repositories", out var repositories) || repositories.ValueKind == JsonValueKind.Null)
            return new("unavailable", null, limitations);
        if (repositories.ValueKind != JsonValueKind.Object) throw Invalid();
        var items = new List<RunRepositoryResource>();
        var aliases = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in repositories.EnumerateObject())
        {
            if (items.Count >= 1000 || string.IsNullOrWhiteSpace(property.Name) || property.Name.Length > 1024
                || !aliases.Add(property.Name) || property.Value.ValueKind != JsonValueKind.Object) throw Invalid();
            string? type = null;
            if (property.Value.TryGetProperty("repository", out var repository) && repository.ValueKind != JsonValueKind.Null)
            {
                if (repository.ValueKind != JsonValueKind.Object) throw Invalid();
                type = Text(repository, "type", 64);
            }
            items.Add(new(transport.Redact(property.Name), type, Text(property.Value, "refName", 2048), Text(property.Value, "version", 1024)));
        }
        string status = items.Count == 0 ? "no_resources_reported"
            : items.Any(item => string.IsNullOrWhiteSpace(item.Version)) ? "versions_unavailable" : "reported_versions";
        return new(status, items, limitations);
    }

    private ResultMetadata Meta(string? requestId, string? continuation = null, string? reason = null, int? scanned = null) => new(
        Organization: organization, Project: project, RequestId: requestId, ContinuationToken: continuation,
        Truncated: reason is not null, Completeness: reason is null ? "complete" : "partial", TruncationReason: reason, ScannedCount: scanned);

    private PipelineInfo ParsePipeline(JsonElement value)
    {
        int id = PositiveId(value);
        string type = "unknown";
        if (value.TryGetProperty("configuration", out var configuration) && configuration.ValueKind != JsonValueKind.Null)
        {
            if (configuration.ValueKind != JsonValueKind.Object) throw Invalid();
            type = Text(configuration, "type", 64) ?? "unknown";
        }
        int? revision = null;
        if (value.TryGetProperty("revision", out var field) && field.ValueKind != JsonValueKind.Null)
        {
            if (field.ValueKind != JsonValueKind.Number || !field.TryGetInt32(out int number) || number < 0) throw Invalid();
            revision = number;
        }
        return new(id, Text(value, "name", 1024) ?? throw Invalid(), Text(value, "folder", 2048), revision, type);
    }

    private PipelineRunInfo ParseRun(JsonElement value, int pipelineId)
    {
        int id = PositiveId(value);
        if (!value.TryGetProperty("pipeline", out var pipeline) || PositiveId(pipeline) != pipelineId) throw Invalid();
        DateTimeOffset? Date(string name)
        {
            if (!value.TryGetProperty(name, out var field) || field.ValueKind == JsonValueKind.Null) return null;
            if (field.ValueKind != JsonValueKind.String || !field.TryGetDateTimeOffset(out var result)) throw Invalid();
            return result;
        }
        // List rows omit resources. Run get adds an allowlisted repository projection separately.
        // Variables, templateParameters, URLs and finalYaml are never included here.
        return new(id, Text(value, "name", 1024), pipelineId, Text(value, "state", 64), Text(value, "result", 64), Date("createdDate"), Date("finishedDate"));
    }

    private string? Text(JsonElement value, string name, int limit)
    {
        if (!value.TryGetProperty(name, out var field) || field.ValueKind == JsonValueKind.Null) return null;
        if (field.ValueKind != JsonValueKind.String || field.GetString()!.Length > limit) throw Invalid();
        return transport.Redact(field.GetString()!);
    }
    private static int PositiveId(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Object || !value.TryGetProperty("id", out var id) || id.ValueKind != JsonValueKind.Number
            || !id.TryGetInt32(out int number) || number <= 0) throw Invalid();
        return number;
    }
    private static JsonElement Array(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Array) return value;
        if (value.ValueKind == JsonValueKind.Object && value.TryGetProperty("value", out var items) && items.ValueKind == JsonValueKind.Array) return items;
        throw Invalid();
    }
    private static AdoException Invalid() => new("invalid_service_response", "The Pipelines response is malformed or violates its endpoint contract.", ExitCode.Transient);
}
