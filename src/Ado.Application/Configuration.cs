namespace Ado.Application;

public sealed record CredentialReference
{
    public string Type { get; init; } = "pat";
    public string Provider { get; init; } = "environment";
    public string? Service { get; init; }
    public string? Account { get; init; }
}

public sealed record PaginationSettings
{
    public int PageSize { get; init; } = 100;
    public int Limit { get; init; } = 100;
    public int MaxItems { get; init; } = 10000;
}

public sealed record TimeoutSettings
{
    public int RequestSeconds { get; init; } = 60;
    public int OperationSeconds { get; init; } = 300;
}

public sealed record DownloadSettings
{
    public long MaxBytes { get; init; } = 1073741824;
    public int TimeoutSeconds { get; init; } = 600;
}

public sealed record Profile
{
    public string? Organization { get; init; }
    public string? Project { get; init; }
    public CredentialReference Authentication { get; init; } = new();
    public string Output { get; init; } = "table";
    public PaginationSettings Pagination { get; init; } = new();
    public TimeoutSettings Timeouts { get; init; } = new();
    public DownloadSettings Downloads { get; init; } = new();
}

public sealed record ConfigurationFile
{
    public int SchemaVersion { get; init; } = 1;
    public string? DefaultProfile { get; init; }
    public Dictionary<string, Profile> Profiles { get; init; } = new(StringComparer.Ordinal);
}

public sealed record ConfigurationOverrides(string? Profile = null, string? Organization = null,
    string? Project = null, string? Output = null, int? Limit = null, int? TimeoutSeconds = null,
    bool ExplicitCredentialSelection = false);

public sealed record EffectiveConfiguration(string? ProfileName, Profile Settings,
    IReadOnlyDictionary<string, string> Sources);
