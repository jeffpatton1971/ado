namespace Ado.Domain;

public sealed record BuildInfo(int Id, string? BuildNumber, int DefinitionId, string? DefinitionName,
    string? Status, string? Result, string? Reason, string? SourceBranch, string? SourceVersion,
    DateTimeOffset? QueueTime, DateTimeOffset? StartTime, DateTimeOffset? FinishTime,
    BuildRepositoryInfo? Repository = null, BuildPullRequestContext? PullRequestContext = null);

public sealed record BuildRepositoryInfo(string? Id, string? Name, string? Type);
public sealed record BuildPullRequestContext(int? Number, string Evidence, string BuiltVersionKind,
    string? HeadVersion, string HeadVersionStatus);
