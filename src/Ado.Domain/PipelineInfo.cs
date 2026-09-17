namespace Ado.Domain;

public sealed record PipelineInfo(int Id, string Name, string? Folder, int? Revision, string ConfigurationType);
public sealed record PipelineRunInfo(int Id, string? Name, int PipelineId, string? State, string? Result,
    DateTimeOffset? CreatedDate, DateTimeOffset? FinishedDate);
public sealed record CollectionResult<T>(IReadOnlyList<T> Items, ResultMetadata Meta);
