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

    public static void ValidateDestination(Uri uri, ServiceHost service, string organization)
    {
        ValidateOrganization(organization);
        if (uri.Scheme != "https" || !uri.IsDefaultPort || uri.UserInfo.Length != 0 || uri.Fragment.Length != 0
            || !uri.Host.Equals(Host(service), StringComparison.OrdinalIgnoreCase)
            || !uri.AbsolutePath.StartsWith("/" + organization + "/_apis/", StringComparison.Ordinal))
            throw new AdoException("unsafe_destination", "The request destination is outside the selected service and organization.", ExitCode.Safety);
    }
}
