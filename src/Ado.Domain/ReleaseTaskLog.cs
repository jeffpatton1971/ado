namespace Ado.Domain;

public sealed record ReleaseTaskLog(int ReleaseId, int EnvironmentId, int DeploymentId, int PhaseId,
    int TaskId, long? StartLine, long? EndLine, IReadOnlyList<string> Lines);
public sealed record ReleaseTaskLogResult(ReleaseTaskLog Data, ResultMetadata Meta);
