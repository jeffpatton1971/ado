namespace Ado.Domain;

public sealed record ReleaseInfo(int Id, string? Name, int DefinitionId, string? DefinitionName,
    string? Status, DateTimeOffset? CreatedOn, DateTimeOffset? ModifiedOn);
