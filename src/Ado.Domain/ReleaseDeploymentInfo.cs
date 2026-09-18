namespace Ado.Domain;

public sealed record ReleaseDeploymentInfo(int Id, int? DeploymentId, int ReleaseId, int EnvironmentId,
    string? EnvironmentName, int? Attempt, string? Status, string? OperationStatus, bool? HasStarted);
