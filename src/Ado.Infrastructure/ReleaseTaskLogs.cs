using Ado.Application;
using Ado.Domain;
using Ado.Infrastructure.Http;

namespace Ado.Infrastructure;

public sealed partial class ReleasesClient
{
    public async Task<ReleaseTaskLogResult> TaskLogAsync(int releaseId, int environmentId, int deploymentId, int taskId,
        long? startLine, long? endLine, int limit, CancellationToken cancellationToken)
    {
        if (environmentId <= 0 || deploymentId <= 0 || taskId <= 0 || limit <= 0 || startLine is < 0 || endLine is < 0
            || (startLine is not null && endLine < startLine))
            throw new AdoException("invalid_log_context", "Supply positive release/environment/deployment/task IDs and valid nonnegative line bounds.", ExitCode.Usage);
        var tasks = await TasksAsync(releaseId, int.MaxValue, cancellationToken);
        var matches = tasks.Items.Where(task => task.EnvironmentId == environmentId && task.DeploymentId == deploymentId && task.Id == taskId).ToArray();
        if (tasks.Meta.Completeness != "complete" || matches.Length != 1 || matches[0].PhaseId is null)
            throw new AdoException("unresolved_task_log", "The expanded release did not uniquely identify the requested task and deployment phase.", ExitCode.Safety);
        int phaseId = matches[0].PhaseId!.Value;
        var baseUri = EndpointBuilder.Release(Operations.ReleaseGet, organization, project, releaseId);
        string Number(long value) => value.ToString(System.Globalization.CultureInfo.InvariantCulture);
        string query = "api-version=7.1";
        if (startLine is not null) query += "&startLine=" + Number(startLine.Value);
        if (endLine is not null) query += "&endLine=" + Number(endLine.Value);
        var uri = new UriBuilder(baseUri)
        {
            Path = baseUri.AbsolutePath + $"/environments/{Number(environmentId)}/deployPhases/{Number(phaseId)}/tasks/{Number(taskId)}/logs",
            Query = query
        }.Uri;
        var response = await transport.GetTextAsync(Operations.ReleaseTaskLog, uri, cancellationToken, project);
        if (response.ContinuationToken is not null)
            throw new AdoException("invalid_service_response", "The release task log response unexpectedly included a continuation token.", ExitCode.Transient);
        var lines = new List<string>();
        int count = 0;
        using var reader = new StringReader(response.Text);
        while (reader.ReadLine() is { } line)
        {
            cancellationToken.ThrowIfCancellationRequested();
            count++;
            if (lines.Count < limit) lines.Add(line);
        }
        bool truncated = count > limit, range = startLine is not null || endLine is not null;
        return new(new(releaseId, environmentId, deploymentId, phaseId, taskId, startLine, endLine, lines),
            new(Organization: organization, Project: project, RequestId: response.RequestId, Truncated: truncated,
                Completeness: truncated ? "partial" : range ? "unknown" : "complete",
                TruncationReason: truncated ? "line_limit" : range ? "requested_range" : null, ScannedCount: count));
    }
}
