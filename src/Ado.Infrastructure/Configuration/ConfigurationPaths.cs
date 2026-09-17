using Ado.Domain;

namespace Ado.Infrastructure.Configuration;

public enum PlatformFamily { Windows, MacOS, Linux }

public sealed record ConfigurationLocation(string Path, string DefaultPath, string Source);

public static class ConfigurationPaths
{
    public static PlatformFamily CurrentPlatform => OperatingSystem.IsWindows() ? PlatformFamily.Windows
        : OperatingSystem.IsMacOS() ? PlatformFamily.MacOS : PlatformFamily.Linux;

    public static string DefaultPath(PlatformFamily platform, string home, string? appData, string? xdg)
    {
        string directory = platform switch
        {
            PlatformFamily.Windows when !string.IsNullOrEmpty(appData) => System.IO.Path.Combine(appData, "ado"),
            PlatformFamily.Windows => throw new AdoException("config_path_unavailable", "The roaming application-data directory is unavailable.", ExitCode.Configuration),
            PlatformFamily.MacOS => System.IO.Path.Combine(home, "Library", "Application Support", "ado"),
            _ => System.IO.Path.Combine(!string.IsNullOrEmpty(xdg) && System.IO.Path.IsPathFullyQualified(xdg)
                ? xdg : System.IO.Path.Combine(home, ".config"), "ado")
        };
        return System.IO.Path.Combine(directory, "config.json");
    }

    public static ConfigurationLocation Resolve(string? explicitPath, Func<string, string?> environment)
    {
        string defaultPath = DefaultPath(CurrentPlatform,
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), environment("XDG_CONFIG_HOME"));
        string? fromEnvironment = environment("ADO_CONFIG");
        string path = explicitPath ?? fromEnvironment ?? defaultPath;
        try
        {
            return new(System.IO.Path.GetFullPath(path), defaultPath,
                explicitPath is not null ? "argument" : fromEnvironment is not null ? "environment" : "default");
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            throw new AdoException("invalid_config_path", "The configuration path is invalid.", ExitCode.Configuration);
        }
    }
}
