namespace Ado.Domain;

public sealed record PipelinePreviewInfo(int PipelineId, bool PreviewRun, int YamlCharacters, string? FinalYaml);
