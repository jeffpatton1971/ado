using System.Text.Json;
using System.Text.Json.Serialization;
using Ado.Application;
using Ado.Domain;

namespace Ado.Infrastructure.Configuration;

public sealed record LoadedConfiguration(ConfigurationFile File, IReadOnlyList<string> Warnings);

public static class ConfigurationLoader
{
    private const int MaxBytes = 1024 * 1024;
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        MaxDepth = 32
    };

    public static async Task<LoadedConfiguration> LoadAsync(ConfigurationLocation location, CancellationToken cancellationToken)
    {
        var warnings = new List<string>();
        try
        {
            await using var stream = new FileStream(location.Path, FileMode.Open, FileAccess.Read, FileShare.Read,
                4096, FileOptions.Asynchronous | FileOptions.SequentialScan);
            if (!OperatingSystem.IsWindows())
            {
                var mode = File.GetUnixFileMode(location.Path);
                if ((mode & (UnixFileMode.GroupRead | UnixFileMode.OtherRead)) != 0)
                    warnings.Add("Configuration is readable by group or other users. Use chmod 700 on its directory and chmod 600 on the file.");
            }
            byte[] bytes = new byte[MaxBytes + 1];
            int count = 0;
            while (count < bytes.Length)
            {
                int read = await stream.ReadAsync(bytes.AsMemory(count), cancellationToken);
                if (read == 0) break;
                count += read;
            }
            if (count > MaxBytes) throw Invalid("Configuration exceeds the 1 MiB size limit.");
            using var document = JsonDocument.Parse(bytes.AsMemory(0, count), new JsonDocumentOptions { MaxDepth = 32 });
            RejectDuplicates(document.RootElement);
            var file = document.Deserialize<ConfigurationFile>(Options) ?? throw Invalid("Configuration must be a JSON object.");
            Validate(file);
            return new(file, warnings);
        }
        catch (FileNotFoundException) when (location.Source == "default") { return new(new(), warnings); }
        catch (DirectoryNotFoundException) when (location.Source == "default") { return new(new(), warnings); }
        catch (JsonException ex)
        {
            // The parser's original message/path can contain attacker-controlled secret values.
            throw Invalid($"Invalid strict JSON or schema at line {(ex.LineNumber ?? 0) + 1}, byte column {(ex.BytePositionInLine ?? 0) + 1}.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw Invalid("The configuration file could not be read. Check its path and access permissions.");
        }
    }

    private static void RejectDuplicates(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in element.EnumerateObject())
            {
                if (!names.Add(property.Name)) throw Invalid("Duplicate JSON properties are not allowed.");
                RejectDuplicates(property.Value);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
            foreach (var value in element.EnumerateArray()) RejectDuplicates(value);
    }

    public static void Validate(ConfigurationFile file)
    {
        if (file.SchemaVersion != 1 || file.Profiles is null) throw Invalid("Unsupported configuration schema.");
        if (file.DefaultProfile is not null && !file.Profiles.ContainsKey(file.DefaultProfile))
            throw Invalid("The default profile does not exist.");
        foreach (var (name, profile) in file.Profiles)
        {
            if (string.IsNullOrWhiteSpace(name) || profile is null) throw Invalid("Profile names and values must be nonempty.");
            ValidateProfile(profile);
        }
    }

    public static void ValidateProfile(Profile profile)
    {
        if (profile.Output is not ("table" or "json")) throw Invalid("Output must be table or json.");
        if (profile.Authentication is null || profile.Authentication.Type is not ("pat" or "entra-token"))
            throw Invalid("Authentication type must be pat or entra-token.");
        if (profile.Authentication.Provider is not ("environment" or "stdin" or "prompt" or "windows-credential-manager" or "macos-keychain" or "linux-secret-service"))
            throw Invalid("Unknown credential provider. Plaintext token configuration is not implemented.");
        if (profile.Authentication.Provider is "windows-credential-manager" or "macos-keychain" or "linux-secret-service")
            if (string.IsNullOrWhiteSpace(profile.Authentication.Service) || string.IsNullOrWhiteSpace(profile.Authentication.Account))
                throw Invalid("Native credential references require both service and account.");
        if (profile.Pagination is null || profile.Pagination.PageSize < 1 || profile.Pagination.Limit < 1
            || profile.Pagination.MaxItems < 1 || profile.Pagination.PageSize > profile.Pagination.MaxItems
            || profile.Pagination.Limit > profile.Pagination.MaxItems)
            throw Invalid("Pagination values must be positive and not exceed maxItems.");
        if (profile.Timeouts is null || profile.Timeouts.RequestSeconds < 1 || profile.Timeouts.OperationSeconds < 1 || profile.Timeouts.OperationSeconds > 86400
            || profile.Timeouts.RequestSeconds > profile.Timeouts.OperationSeconds)
            throw Invalid("Timeouts must be positive and no greater than one day; request timeout cannot exceed operation timeout.");
        if (profile.Downloads is null || profile.Downloads.MaxBytes < 1 || profile.Downloads.TimeoutSeconds is < 1 or > 86400)
            throw Invalid("Download limits must be positive.");
    }

    private static AdoException Invalid(string message) => new("invalid_configuration", message, ExitCode.Configuration);
}
