using Ado.Application;
using Ado.Domain;

namespace Ado.Infrastructure.Configuration;

public static class ConfigurationResolver
{
    public static EffectiveConfiguration Resolve(ConfigurationFile file, ConfigurationOverrides arguments,
        Func<string, string?> environment)
    {
        string? name = arguments.Profile ?? environment("ADO_PROFILE") ?? file.DefaultProfile;
        Profile profile = new();
        if (name is not null && !file.Profiles.TryGetValue(name, out profile!))
            throw new AdoException("profile_not_found", "The selected profile does not exist.", ExitCode.Configuration);
        var sources = new Dictionary<string, string>(StringComparer.Ordinal);
        string? Pick(string key, string? argument, string? configured)
        {
            string? env = environment("ADO_" + key.ToUpperInvariant());
            sources[key] = argument is not null ? "argument" : env is not null ? "environment" : name is not null ? "profile" : "default";
            return argument ?? env ?? configured;
        }
        string? organization = Pick("organization", arguments.Organization, profile.Organization);
        string? project = Pick("project", arguments.Project, profile.Project);
        string output = Pick("output", arguments.Output, profile.Output)!;
        int limit = Number(Pick("limit", arguments.Limit?.ToString(System.Globalization.CultureInfo.InvariantCulture),
            profile.Pagination.Limit.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        int timeout = Number(Pick("timeout", arguments.TimeoutSeconds?.ToString(System.Globalization.CultureInfo.InvariantCulture),
            profile.Timeouts.RequestSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        // Do not carry an organization-bound native reference to another organization.
        if (!arguments.ExplicitCredentialSelection && !string.Equals(organization, profile.Organization, StringComparison.OrdinalIgnoreCase)
            && profile.Authentication.Provider is "windows-credential-manager" or "macos-keychain" or "linux-secret-service")
            throw new AdoException("credential_organization_mismatch", "Changing organization requires an explicit credential selection; select an appropriate profile.", ExitCode.Configuration);
        var settings = profile with
        {
            Organization = organization,
            Project = project,
            Output = output,
            Pagination = profile.Pagination with { Limit = limit },
            Timeouts = profile.Timeouts with { RequestSeconds = timeout }
        };
        ConfigurationLoader.ValidateProfile(settings);
        sources["authentication"] = name is null ? "default" : "profile";
        return new(name, settings, sources);
    }

    private static int Number(string? value) => int.TryParse(value, System.Globalization.NumberStyles.None,
        System.Globalization.CultureInfo.InvariantCulture, out int number) && number > 0 ? number
        : throw new AdoException("invalid_configuration", "Limit and timeout must be positive integers.", ExitCode.Configuration);
}
