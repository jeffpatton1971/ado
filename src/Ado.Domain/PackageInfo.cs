namespace Ado.Domain;

public sealed record PackageInfo(Guid Id, string Name, string? NormalizedName, string ProtocolType);
public sealed record PackageVersionInfo(Guid Id, Guid PackageId, string Version, string? NormalizedVersion,
    bool? IsListed, bool? IsDeleted, bool? IsLatest, DateTimeOffset? PublishDate);
public sealed record PackageResolution(string Feed, PackageInfo Package, PackageVersionInfo Version, string Match,
    IReadOnlyList<string> Limitations);
