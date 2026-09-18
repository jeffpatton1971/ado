using System.Text.Json;
using Ado.Application;
using Ado.Domain;
using Ado.Infrastructure.Http;

namespace Ado.Infrastructure;

public sealed partial class ReleasesClient
{
    public async Task<CollectionResult<ReleaseDeploymentInfo>> DeploymentsAsync(int releaseId, int limit, CancellationToken cancellationToken)
    {
        if (limit < 1) throw new AdoException("invalid_limit", "The deployment limit must be positive.", ExitCode.Usage);
        using var response = await transport.GetAsync(Operations.ReleaseDeployments,
            EndpointBuilder.Release(Operations.ReleaseGet, organization, project, releaseId), cancellationToken, project);
        var root = response.Document.RootElement;
        if (Parse(root).Id != releaseId || response.ContinuationToken is not null
            || !root.TryGetProperty("environments", out var environments) || environments.ValueKind != JsonValueKind.Array) throw Invalid();
        var items = new List<ReleaseDeploymentInfo>();
        var environmentIds = new HashSet<int>();
        var stepIds = new HashSet<(int EnvironmentId, int Id)>();
        int count = 0;
        bool missing = false;
        foreach (var environment in environments.EnumerateArray())
        {
            int environmentId = PositiveId(environment);
            if (!environmentIds.Add(environmentId)) throw Invalid();
            if (OptionalPositive(environment, "releaseId") is { } returnedReleaseId && returnedReleaseId != releaseId) throw Invalid();
            string? name = Text(environment, "name", 1024);
            if (!environment.TryGetProperty("deploySteps", out var steps) || steps.ValueKind == JsonValueKind.Null)
            {
                missing = true;
                continue;
            }
            if (steps.ValueKind != JsonValueKind.Array) throw Invalid();
            foreach (var step in steps.EnumerateArray())
            {
                int id = PositiveId(step);
                if (!stepIds.Add((environmentId, id))) throw Invalid();
                bool? started = null;
                if (step.TryGetProperty("hasStarted", out var flag) && flag.ValueKind != JsonValueKind.Null)
                {
                    if (flag.ValueKind is not (JsonValueKind.True or JsonValueKind.False)) throw Invalid();
                    started = flag.GetBoolean();
                }
                var item = new ReleaseDeploymentInfo(id, OptionalPositive(step, "deploymentId"), releaseId, environmentId,
                    name, OptionalPositive(step, "attempt"), Text(step, "status", 64), Text(step, "operationStatus", 64), started);
                count++;
                if (items.Count < limit) items.Add(item);
            }
        }
        bool truncated = count > limit;
        return new(items, new(Organization: organization, Project: project, RequestId: response.RequestId,
            Truncated: truncated, Completeness: truncated ? "partial" : missing ? "unknown" : "complete",
            TruncationReason: truncated ? "item_limit" : missing ? "deployment_arrays_missing" : null, ScannedCount: count));
    }
}
