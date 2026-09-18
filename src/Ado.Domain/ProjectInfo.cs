namespace Ado.Domain;

public sealed record ProjectInfo(Guid Id, string Name, string? Description, string? State, string? Visibility);
public sealed record ResultMetadata(int SchemaVersion = 1, string? Organization = null, string? Project = null,
    string? RequestId = null, string? ContinuationToken = null, bool Truncated = false,
    string Completeness = "complete", string? TruncationReason = null, int? ScannedCount = null);
public sealed record ProjectResult(IReadOnlyList<ProjectInfo> Projects, ResultMetadata Meta);
