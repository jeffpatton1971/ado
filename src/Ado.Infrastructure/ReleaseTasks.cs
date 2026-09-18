using System.Text.Json;
using Ado.Application;
using Ado.Domain;
using Ado.Infrastructure.Http;

namespace Ado.Infrastructure;

public sealed partial class ReleasesClient
{
    public async Task<CollectionResult<ReleaseTaskInfo>> TasksAsync(int releaseId, int limit, CancellationToken cancellationToken)
    {
        if (limit < 1) throw new AdoException("invalid_limit", "The task limit must be positive.", ExitCode.Usage);
        var baseUri = EndpointBuilder.Release(Operations.ReleaseGet, organization, project, releaseId);
        var uri = new UriBuilder(baseUri) { Query = baseUri.Query.TrimStart('?') + "&%24expand=tasks" }.Uri;
        using var response = await transport.GetAsync(Operations.ReleaseTasks, uri, cancellationToken, project);
        var root = response.Document.RootElement;
        if (Parse(root).Id != releaseId || response.ContinuationToken is not null
            || !root.TryGetProperty("environments", out var environments) || environments.ValueKind != JsonValueKind.Array) throw Invalid();
        var items = new List<ReleaseTaskInfo>();
        bool missing = false;
        int count = 0;
        IEnumerable<JsonElement> Children(JsonElement parent, string field)
        {
            if (parent.ValueKind != JsonValueKind.Object) throw Invalid();
            if (!parent.TryGetProperty(field, out var array) || array.ValueKind == JsonValueKind.Null)
            {
                missing = true;
                return [];
            }
            if (array.ValueKind != JsonValueKind.Array) throw Invalid();
            return array.EnumerateArray();
        }
        var environmentIds = new HashSet<int>();
        foreach (var environment in environments.EnumerateArray())
        {
            int environmentId = PositiveId(environment);
            if (!environmentIds.Add(environmentId)) throw Invalid();
            if (OptionalPositive(environment, "releaseId") is { } returnedId && returnedId != releaseId) throw Invalid();
            var stepIds = new HashSet<int>();
            foreach (var step in Children(environment, "deploySteps"))
            {
                int stepId = PositiveId(step);
                if (!stepIds.Add(stepId)) throw Invalid();
                int? deploymentId = OptionalPositive(step, "deploymentId"), attempt = OptionalPositive(step, "attempt");
                foreach (var phase in Children(step, "releaseDeployPhases"))
                {
                    if (phase.ValueKind != JsonValueKind.Object) throw Invalid();
                    string? phaseName = Text(phase, "name", 1024);
                    foreach (var job in Children(phase, "deploymentJobs"))
                    {
                        if (job.ValueKind != JsonValueKind.Object) throw Invalid();
                        int? jobId = null;
                        string? jobName = null;
                        if (job.TryGetProperty("job", out var parent) && parent.ValueKind != JsonValueKind.Null)
                        {
                            jobId = PositiveId(parent);
                            jobName = Text(parent, "name", 1024);
                        }
                        var taskIds = new HashSet<int>();
                        foreach (var task in Children(job, "tasks"))
                        {
                            int taskId = PositiveId(task);
                            if (!taskIds.Add(taskId)) throw Invalid();
                            long? lineCount = null;
                            if (task.TryGetProperty("lineCount", out var lines) && lines.ValueKind != JsonValueKind.Null)
                            {
                                if (lines.ValueKind != JsonValueKind.Number || !lines.TryGetInt64(out long number) || number < 0) throw Invalid();
                                lineCount = number;
                            }
                            var item = new ReleaseTaskInfo(taskId, releaseId, environmentId, stepId, deploymentId, attempt,
                                phaseName, jobId, jobName, Text(task, "name", 1024), Text(task, "status", 64), lineCount, OptionalPositive(phase, "phaseId"));
                            count++;
                            if (items.Count < limit) items.Add(item);
                        }
                    }
                }
            }
        }
        bool truncated = count > limit;
        return new(items, new(Organization: organization, Project: project, RequestId: response.RequestId,
            Truncated: truncated, Completeness: truncated ? "partial" : missing ? "unknown" : "complete",
            TruncationReason: truncated ? "item_limit" : missing ? "task_arrays_missing" : null, ScannedCount: count));
    }
}
