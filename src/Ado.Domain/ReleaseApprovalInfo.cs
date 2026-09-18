namespace Ado.Domain;

public sealed record ReleaseApprovalInfo(int Id, int ReleaseId, int EnvironmentId, string? EnvironmentName,
    string Phase, string? Status, bool? IsAutomated, int? Attempt, int? Rank);
