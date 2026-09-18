namespace Ado.Domain;

public sealed record ReleaseEnvironmentInfo(int Id, int ReleaseId, int? DefinitionEnvironmentId,
    string? Name, string? Status, int? Rank);
