namespace Ado.Domain;

public sealed record BuildTimelineRecord(Guid Id, Guid? ParentId, int BuildId, string? Name,
    string? Type, string? State, string? Result, int? LogId, int? Order, int? ErrorCount, int? WarningCount);
