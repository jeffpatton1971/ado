using System.Text.RegularExpressions;
using Ado.Application;
using Ado.Domain;

namespace Ado.Infrastructure.Http;

public static partial class EndpointBuilder
{
    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9-]{0,49}$", RegexOptions.CultureInvariant)]
    private static partial Regex OrganizationPattern();

    public static string ValidateOrganization(string? organization) => organization is not null && OrganizationPattern().IsMatch(organization)
        ? organization : throw new AdoException("invalid_organization", "Supply an Azure DevOps organization name, not a URL.", ExitCode.Usage);

    public static string Host(ServiceHost service) => service switch
    {
        ServiceHost.Core => "dev.azure.com",
        ServiceHost.Release => "vsrm.dev.azure.com",
        ServiceHost.Feeds => "feeds.dev.azure.com",
        _ => throw new AdoException("unsupported_service", "The service host is not supported.", ExitCode.Usage)
    };

    public static Uri Project(OperationDescriptor operation, string organization, string? project = null,
        IReadOnlyDictionary<string, string?>? query = null)
    {
        ValidateOrganization(organization);
        if (operation != Operations.ProjectGet && operation != Operations.ProjectList)
            throw new AdoException("unsupported_operation", "This endpoint is not registered.", ExitCode.Usage);
        string path = $"/{organization}/_apis/projects";
        if (operation.RequiresProject)
        {
            if (string.IsNullOrWhiteSpace(project) || project is "." or ".." || project.Any(c => char.IsControl(c) || c is '/' or '\\'))
                throw new AdoException("invalid_project", "Supply a valid project name or ID.", ExitCode.Usage);
            path += "/" + Uri.EscapeDataString(project);
        }
        var parameters = new List<string> { "api-version=" + Uri.EscapeDataString(operation.ApiVersion) };
        if (query is not null)
            foreach (var (key, value) in query)
            {
                if (key is not ("$top" or "continuationToken"))
                    throw new AdoException("invalid_query_parameter", "This query parameter is not registered.", ExitCode.Usage);
                if (value is not null) parameters.Add(Uri.EscapeDataString(key) + "=" + Uri.EscapeDataString(value));
            }
        return new UriBuilder("https", Host(operation.Service)) { Path = path, Query = string.Join('&', parameters) }.Uri;
    }

    public static string ProjectSegment(string? project)
    {
        if (string.IsNullOrWhiteSpace(project) || project is "." or ".." || project.Any(c => char.IsControl(c) || c is '/' or '\\'))
            throw new AdoException("invalid_project", "Supply a valid project name or ID.", ExitCode.Usage);
        return Uri.EscapeDataString(project);
    }

    public static Uri Pipeline(OperationDescriptor operation, string organization, string project, int? pipelineId = null,
        int? runId = null, int? top = null, string? continuation = null)
    {
        ValidateOrganization(organization);
        string path = $"/{organization}/{ProjectSegment(project)}/_apis/pipelines";
        if (operation != Operations.PipelineList)
        {
            if (operation != Operations.PipelineGet && operation != Operations.PipelineRuns && operation != Operations.PipelineRunGet && operation != Operations.PipelineRunStart)
                throw new AdoException("unsupported_operation", "This endpoint is not registered.", ExitCode.Usage);
            if (pipelineId is null or <= 0) throw new AdoException("pipeline_required", "Supply a positive --pipeline-id.", ExitCode.Usage);
            path += "/" + pipelineId.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }
        if (operation == Operations.PipelineRuns || operation == Operations.PipelineRunGet || operation == Operations.PipelineRunStart) path += "/runs";
        if (operation == Operations.PipelineRunGet)
        {
            if (runId is null or <= 0) throw new AdoException("run_required", "Supply a positive --run-id.", ExitCode.Usage);
            path += "/" + runId.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }
        string query = "api-version=" + operation.ApiVersion;
        if (top is not null || continuation is not null)
        {
            if (operation != Operations.PipelineList)
                throw new AdoException("unsupported_pagination", "This Pipelines operation has no documented paging parameters.", ExitCode.Usage);
            if (top is <= 0) throw new AdoException("invalid_pagination", "Page size must be positive.", ExitCode.Usage);
            if (continuation is { Length: 0 or > 2048 } || continuation?.Any(char.IsControl) == true)
                throw new AdoException("invalid_pagination", "The continuation token exceeds its bounds or contains control characters.", ExitCode.Usage);
            if (top is not null) query += "&%24top=" + top.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
            if (continuation is not null) query += "&continuationToken=" + Uri.EscapeDataString(continuation);
        }
        return new UriBuilder("https", Host(operation.Service)) { Path = path, Query = query }.Uri;
    }

    public static void ValidateDestination(Uri uri, ServiceHost service, string organization, string? project = null)
    {
        ValidateOrganization(organization);
        if (uri.Scheme != "https" || !uri.IsDefaultPort || uri.UserInfo.Length != 0 || uri.Fragment.Length != 0
            || !uri.Host.Equals(Host(service), StringComparison.OrdinalIgnoreCase)
            || !uri.AbsolutePath.StartsWith("/" + organization + "/" + (project is null ? "" : ProjectSegment(project) + "/") + "_apis/", StringComparison.Ordinal))
            throw new AdoException("unsafe_destination", "The request destination is outside the selected service and organization.", ExitCode.Safety);
    }
}
