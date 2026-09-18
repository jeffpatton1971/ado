namespace Ado.Domain;

public sealed record FeedInfo(Guid Id, string Name, string Scope, Guid? ProjectId, string? ProjectName);
