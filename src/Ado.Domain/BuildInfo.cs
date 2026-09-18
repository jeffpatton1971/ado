namespace Ado.Domain;

public sealed record BuildInfo(int Id, string? BuildNumber, int DefinitionId, string? DefinitionName,
    string? Status, string? Result, string? Reason, string? SourceBranch, string? SourceVersion,
    DateTimeOffset? QueueTime, DateTimeOffset? StartTime, DateTimeOffset? FinishTime);
