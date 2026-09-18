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
            if (operation != Operations.PipelineGet && operation != Operations.PipelineRuns && operation != Operations.PipelineRunGet && operation != Operations.PipelineRunStart && operation != Operations.PipelineRunPreview)
                throw new AdoException("unsupported_operation", "This endpoint is not registered.", ExitCode.Usage);
            if (pipelineId is null or <= 0) throw new AdoException("pipeline_required", "Supply a positive --pipeline-id.", ExitCode.Usage);
            path += "/" + pipelineId.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }
        if (operation == Operations.PipelineRuns || operation == Operations.PipelineRunGet || operation == Operations.PipelineRunStart || operation == Operations.PipelineRunPreview) path += "/runs";
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

    public static Uri Build(OperationDescriptor operation, string organization, string project, int? buildId = null,
        int? top = null, string? continuation = null, BuildFilters? filters = null)
    {
        ValidateOrganization(organization);
        string path = $"/{organization}/{ProjectSegment(project)}/_apis/build/builds";
        var query = new List<string> { "api-version=" + operation.ApiVersion };
        if (operation == Operations.BuildGet)
        {
            if (buildId is null or <= 0) throw new AdoException("build_required", "Supply a positive --build-id.", ExitCode.Usage);
            if (top is not null || continuation is not null || filters is not null)
                throw new AdoException("invalid_build_query", "Build get does not support list filters or pagination.", ExitCode.Usage);
            path += "/" + buildId.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }
        else if (operation == Operations.BuildList)
        {
            filters ??= new();
            filters.Validate();
            if (top is <= 0 || continuation is { Length: 0 or > 2048 } || continuation?.Any(char.IsControl) == true)
                throw new AdoException("invalid_pagination", "Build page size or continuation token is invalid.", ExitCode.Usage);
            void Add(string key, string? value)
            {
                if (value is not null) query.Add(Uri.EscapeDataString(key) + "=" + Uri.EscapeDataString(value));
            }
            Add("$top", top?.ToString(System.Globalization.CultureInfo.InvariantCulture));
            Add("continuationToken", continuation);
            Add("definitions", filters.DefinitionId?.ToString(System.Globalization.CultureInfo.InvariantCulture));
            Add("statusFilter", filters.Status);
            Add("resultFilter", filters.Result);
            Add("branchName", filters.EffectiveBranch);
            if (filters.PrNumber is not null) Add("reasonFilter", "pullRequest");
            Add("repositoryId", filters.RepositoryId);
            Add("repositoryType", filters.RepositoryType);
            Add("queryOrder", "queueTimeDescending");
        }
        else throw new AdoException("unsupported_operation", "This endpoint is not registered.", ExitCode.Usage);
        return new UriBuilder("https", Host(operation.Service)) { Path = path, Query = string.Join('&', query) }.Uri;
    }

    public static Uri BuildLog(OperationDescriptor operation, string organization, string project, int buildId,
        int? logId = null, long? startLine = null, long? endLine = null)
    {
        ValidateOrganization(organization);
        if (buildId <= 0) throw new AdoException("build_required", "Supply a positive --build-id.", ExitCode.Usage);
        string path = $"/{organization}/{ProjectSegment(project)}/_apis/build/builds/{buildId.ToString(System.Globalization.CultureInfo.InvariantCulture)}/logs";
        string query = "api-version=" + operation.ApiVersion;
        if (operation == Operations.BuildLogGet)
        {
            if (logId is null or <= 0) throw new AdoException("log_required", "Supply a positive --log-id.", ExitCode.Usage);
            if (startLine is < 0 || endLine is < 0 || (startLine is not null && endLine is not null && endLine < startLine))
                throw new AdoException("invalid_line_range", "Line positions must be nonnegative and end-line must not precede start-line.", ExitCode.Usage);
            path += "/" + logId.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
            if (startLine is not null) query += "&startLine=" + startLine.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
            if (endLine is not null) query += "&endLine=" + endLine.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }
        else if (operation != Operations.BuildLogs || logId is not null || startLine is not null || endLine is not null)
            throw new AdoException("unsupported_operation", "This log endpoint or query is not registered.", ExitCode.Usage);
        return new UriBuilder("https", Host(operation.Service)) { Path = path, Query = query }.Uri;
    }

    public static Uri BuildArtifact(OperationDescriptor operation, string organization, string project, int buildId, string? artifactName = null)
    {
        ValidateOrganization(organization);
        if (buildId <= 0) throw new AdoException("build_required", "Supply a positive --build-id.", ExitCode.Usage);
        string path = $"/{organization}/{ProjectSegment(project)}/_apis/build/builds/{buildId.ToString(System.Globalization.CultureInfo.InvariantCulture)}/artifacts";
        string query = "api-version=" + operation.ApiVersion;
        if (operation == Operations.BuildArtifactGet)
        {
            if (string.IsNullOrWhiteSpace(artifactName) || artifactName.Length > 1024 || artifactName.Any(char.IsControl))
                throw new AdoException("artifact_name_required", "Supply --artifact-name with a nonempty build-output name, at most 1024 characters and without controls.", ExitCode.Usage);
            query += "&artifactName=" + Uri.EscapeDataString(artifactName);
        }
        else if (operation != Operations.BuildArtifactList || artifactName is not null)
            throw new AdoException("unsupported_operation", "This build-output endpoint is not registered.", ExitCode.Usage);
        return new UriBuilder("https", Host(operation.Service)) { Path = path, Query = query }.Uri;
    }

    public static Uri PipelineArtifact(string organization, string project, int pipelineId, int runId, string artifactName)
    {
        // Reuse the input validation for artifact names and pipeline/run identities.
        _ = BuildArtifact(Operations.BuildArtifactGet, organization, project, runId, artifactName);
        var run = Pipeline(Operations.PipelineRunGet, organization, project, pipelineId, runId);
        return new UriBuilder(run)
        {
            Path = run.AbsolutePath + "/artifacts",
            Query = "api-version=7.1&artifactName=" + Uri.EscapeDataString(artifactName) + "&%24expand=signedContent"
        }.Uri;
    }

    public static Uri Release(OperationDescriptor operation, string organization, string project, int? releaseId = null,
        int? top = null, string? continuation = null, int? definitionId = null)
    {
        ValidateOrganization(organization);
        string path = $"/{organization}/{ProjectSegment(project)}/_apis/release/releases";
        string query = "api-version=" + operation.ApiVersion;
        if (operation == Operations.ReleaseGet)
        {
            if (releaseId is null or <= 0) throw new AdoException("release_required", "Supply a positive --release-id.", ExitCode.Usage);
            if (top is not null || continuation is not null || definitionId is not null)
                throw new AdoException("invalid_release_query", "Release get does not support list filters or pagination.", ExitCode.Usage);
            path += "/" + releaseId.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }
        else if (operation == Operations.ReleaseList)
        {
            if (releaseId is not null || top is <= 0 || definitionId is <= 0)
                throw new AdoException("invalid_release_query", "Release list requires positive page size and definition ID when supplied.", ExitCode.Usage);
            if (continuation is not null && (continuation.Length is 0 or > 10 || !continuation.All(char.IsAsciiDigit)
                || !int.TryParse(continuation, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out _)))
                throw new AdoException("invalid_pagination", "Release continuation tokens must be nonnegative 32-bit integers.", ExitCode.Usage);
            query += "&queryOrder=descending";
            if (top is not null) query += "&%24top=" + top.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
            if (continuation is not null) query += "&continuationToken=" + continuation;
            if (definitionId is not null) query += "&definitionId=" + definitionId.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }
        else throw new AdoException("unsupported_operation", "This release endpoint is not registered.", ExitCode.Usage);
        return new UriBuilder("https", Host(ServiceHost.Release)) { Path = path, Query = query }.Uri;
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
