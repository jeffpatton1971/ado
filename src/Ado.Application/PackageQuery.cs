using System.Globalization;
using Ado.Domain;

namespace Ado.Application;

public sealed record PackageQuery(string? PackageId = null, string? VersionId = null, string? Protocol = null, string? Name = null, string? Version = null)
{
    public static int Offset(string? token) => token is null ? 0
        : int.TryParse(token, NumberStyles.None, CultureInfo.InvariantCulture, out int offset) && offset >= 0 ? offset
        : throw new AdoException("invalid_pagination", "Package continuation must be a nonnegative integer offset.", ExitCode.Usage);

    public void Validate(string command)
    {
        if (command == "package resolve")
        {
            if (string.IsNullOrEmpty(Name) || Name.Length > 100 || Name.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not ('.' or '_' or '-')))
                throw new AdoException("invalid_package_name", "Supply an exact NuGet --name using letters, digits, dots, underscores or hyphens.", ExitCode.Usage);
            if (string.IsNullOrEmpty(Version) || Version.Length > 256 || Version.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not ('.' or '_' or '-' or '+')))
                throw new AdoException("invalid_package_version", "Supply a literal --package-version; ranges, wildcards and whitespace are not supported.", ExitCode.Usage);
        }
        if (command is not ("package list" or "package resolve") && (!Guid.TryParse(PackageId, out var id) || id == Guid.Empty))
            throw new AdoException("invalid_package_id", "Supply a package GUID with --package-id (not its name).", ExitCode.Usage);
        if (command == "package version get" && (!Guid.TryParse(VersionId, out var versionId) || versionId == Guid.Empty))
            throw new AdoException("invalid_version_id", "Supply a version GUID with --version-id (not a version string).", ExitCode.Usage);
        foreach (string? value in new[] { Protocol, Name })
            if (value is not null && (string.IsNullOrWhiteSpace(value) || value.Length > 256 || value.Any(char.IsControl)))
                throw new AdoException("invalid_package_filter", "Package filters must be nonempty, at most 256 characters and contain no controls.", ExitCode.Usage);
    }
}
