namespace Ado.Domain;

public sealed record ReleaseTaskInfo(int Id, int ReleaseId, int EnvironmentId, int StepId,
    int? DeploymentId, int? Attempt, string? PhaseName, int? JobId, string? JobName,
    string? Name, string? Status, long? LineCount, int? PhaseId = null);
