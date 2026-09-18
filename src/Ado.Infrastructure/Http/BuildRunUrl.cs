using System.Globalization;
using Ado.Domain;

namespace Ado.Infrastructure.Http;

public sealed record BuildRunUrl(string Organization, string Project, int BuildId)
{
    public static BuildRunUrl Parse(string value)
    {
        if (value.Length > 8192 || value.Any(c => char.IsControl(c) || c == '\\')
            || !Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme != "https"
            || uri.Port != 443 || uri.UserInfo.Length != 0) throw Invalid();
        string[] parts = uri.AbsolutePath.Split('/');
        string organization;
        string project;
        if (uri.Host.Equals("dev.azure.com", StringComparison.OrdinalIgnoreCase) && parts.Length == 5
            && parts[3] == "_build" && parts[4] == "results")
        {
            organization = Uri.UnescapeDataString(parts[1]);
            project = Uri.UnescapeDataString(parts[2]);
        }
        else if (uri.Host.EndsWith(".visualstudio.com", StringComparison.OrdinalIgnoreCase)
            && parts.Length == 4 && parts[2] == "_build" && parts[3] == "results")
        {
            organization = uri.Host[..^".visualstudio.com".Length];
            project = Uri.UnescapeDataString(parts[1]);
        }
        else throw Invalid();
        // Never fetch this URL. Only validated identity fields enter endpoint construction.
        try { EndpointBuilder.ValidateOrganization(organization); EndpointBuilder.ProjectSegment(project); }
        catch (AdoException) { throw Invalid(); }
        int? buildId = null;
        foreach (string pair in uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            string[] parameter = pair.Split('=', 2);
            string key = Uri.UnescapeDataString(parameter[0]);
            if (!key.Equals("buildId", StringComparison.OrdinalIgnoreCase)) continue;
            if (buildId is not null || parameter.Length != 2
                || !int.TryParse(Uri.UnescapeDataString(parameter[1]), NumberStyles.None, CultureInfo.InvariantCulture, out int id)
                || id <= 0) throw Invalid();
            buildId = id;
        }
        if (buildId is null) throw Invalid();
        return new(organization, project, buildId.Value);
    }

    public void ValidateContext(string? organization, string? project, int? buildId)
    {
        if ((organization is not null && !string.Equals(organization, Organization, StringComparison.OrdinalIgnoreCase))
            || (project is not null && !string.Equals(project, Project, StringComparison.OrdinalIgnoreCase))
            || (buildId is not null && buildId != BuildId))
            throw new AdoException("run_url_context_mismatch", "The run URL conflicts with the selected organization, project or build ID. Select matching context explicitly.", ExitCode.Usage);
    }

    private static AdoException Invalid() => new("invalid_run_url",
        "Supply an HTTPS Azure DevOps build results URL with one positive buildId; only dev.azure.com and organization.visualstudio.com are supported.", ExitCode.Usage);
}
