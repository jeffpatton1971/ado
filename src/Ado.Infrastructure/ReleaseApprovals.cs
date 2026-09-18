using System.Text.Json;
using Ado.Application;
using Ado.Domain;
using Ado.Infrastructure.Http;

namespace Ado.Infrastructure;

public sealed partial class ReleasesClient
{
    public async Task<CollectionResult<ReleaseApprovalInfo>> ApprovalsAsync(int releaseId, int limit, CancellationToken cancellationToken)
    {
        if (limit < 1) throw new AdoException("invalid_limit", "The approval limit must be positive.", ExitCode.Usage);
        using var response = await transport.GetAsync(Operations.ReleaseApprovals,
            EndpointBuilder.Release(Operations.ReleaseGet, organization, project, releaseId), cancellationToken, project);
        var root = response.Document.RootElement;
        if (Parse(root).Id != releaseId || response.ContinuationToken is not null
            || !root.TryGetProperty("environments", out var environments) || environments.ValueKind != JsonValueKind.Array) throw Invalid();
        var items = new List<ReleaseApprovalInfo>();
        var environmentsSeen = new HashSet<int>();
        var approvalsSeen = new HashSet<int>();
        int count = 0;
        bool missing = false;
        foreach (var environment in environments.EnumerateArray())
        {
            int environmentId = PositiveId(environment);
            if (!environmentsSeen.Add(environmentId)) throw Invalid();
            if (OptionalPositive(environment, "releaseId") is { } returnedReleaseId && returnedReleaseId != releaseId) throw Invalid();
            string? environmentName = Text(environment, "name", 1024);
            foreach (var (field, phase) in new[] { ("preDeployApprovals", "preDeploy"), ("postDeployApprovals", "postDeploy") })
            {
                if (!environment.TryGetProperty(field, out var approvals) || approvals.ValueKind == JsonValueKind.Null)
                {
                    missing = true;
                    continue;
                }
                if (approvals.ValueKind != JsonValueKind.Array) throw Invalid();
                foreach (var approval in approvals.EnumerateArray())
                {
                    int id = PositiveId(approval);
                    if (!approvalsSeen.Add(id)) throw Invalid();
                    CheckReference(approval, "release", releaseId);
                    CheckReference(approval, "releaseEnvironment", environmentId);
                    bool? automated = null;
                    if (approval.TryGetProperty("isAutomated", out var flag) && flag.ValueKind != JsonValueKind.Null)
                    {
                        if (flag.ValueKind is not (JsonValueKind.True or JsonValueKind.False)) throw Invalid();
                        automated = flag.GetBoolean();
                    }
                    var item = new ReleaseApprovalInfo(id, releaseId, environmentId, environmentName, phase,
                        Text(approval, "status", 64), automated, ApprovalNumber(approval, "attempt"), ApprovalNumber(approval, "rank"));
                    count++;
                    if (items.Count < limit) items.Add(item);
                }
            }
        }
        bool truncated = count > limit;
        return new(items, new(Organization: organization, Project: project, RequestId: response.RequestId,
            Truncated: truncated, Completeness: truncated ? "partial" : missing ? "unknown" : "complete",
            TruncationReason: truncated ? "item_limit" : missing ? "approval_arrays_missing" : null, ScannedCount: count));
    }

    private static void CheckReference(JsonElement value, string field, int expected)
    {
        if (value.TryGetProperty(field, out var reference) && reference.ValueKind != JsonValueKind.Null
            && PositiveId(reference) != expected) throw Invalid();
    }

    private static int? ApprovalNumber(JsonElement value, string field)
    {
        if (!value.TryGetProperty(field, out var number) || number.ValueKind == JsonValueKind.Null) return null;
        if (number.ValueKind != JsonValueKind.Number || !number.TryGetInt32(out int result) || result < 0) throw Invalid();
        return result;
    }
}
