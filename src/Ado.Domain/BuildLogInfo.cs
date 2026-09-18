namespace Ado.Domain;

public sealed record BuildLogInfo(int Id, int BuildId, long? LineCount, string? Type,
    DateTimeOffset? CreatedOn, DateTimeOffset? LastChangedOn);
public sealed record BuildLogContent(int BuildId, int LogId, long? StartLine, long? EndLine, IReadOnlyList<string> Lines);
public sealed record BuildLogResult(BuildLogContent Data, ResultMetadata Meta);
