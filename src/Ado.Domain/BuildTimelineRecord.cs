namespace Ado.Domain;

public sealed record BuildTimelineRecord(Guid Id, Guid? ParentId, int BuildId, string? Name,
    string? Type, string? State, string? Result, int? LogId, int? Order, int? ErrorCount, int? WarningCount,
    int? Attempt = null, string? Identifier = null, Guid? TimelineId = null, Guid? DetailsTimelineId = null,
    IReadOnlyList<BuildTimelineAttempt>? PreviousAttempts = null,
    DateTimeOffset? StartTime = null, DateTimeOffset? FinishTime = null);

public sealed record BuildTimelineAttempt(int Attempt, Guid RecordId, Guid TimelineId);
