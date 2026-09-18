namespace Ado.Domain;

public sealed record BuildOutputArtifactInfo(int Id, int BuildId, string Name, string ResourceType, string? Source);
