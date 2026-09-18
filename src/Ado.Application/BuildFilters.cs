using Ado.Domain;

namespace Ado.Application;

public sealed record BuildFilters(int? DefinitionId = null, string? Status = null, string? Result = null, string? Branch = null,
    string? RepositoryId = null, string? RepositoryType = null, string? SourceSha = null, int? PrNumber = null)
{
    public string? EffectiveBranch => PrNumber is { } number
        ? "refs/pull/" + number.ToString(System.Globalization.CultureInfo.InvariantCulture) + "/merge" : Branch;

    public void Validate()
    {
        if (PrNumber is <= 0)
            throw new AdoException("invalid_pr_number", "--pr-number must be positive.", ExitCode.Usage);
        if (PrNumber is not null)
        {
            if (string.IsNullOrWhiteSpace(RepositoryId) ||
                !(string.Equals(RepositoryType, "GitHub", StringComparison.OrdinalIgnoreCase) || string.Equals(RepositoryType, "TfsGit", StringComparison.OrdinalIgnoreCase)))
                throw new AdoException("pr_repository_required", "--pr-number requires --repository-id and --repository-type GitHub or TfsGit.", ExitCode.Usage);
            if (Branch is not null && !string.Equals(Branch, EffectiveBranch, StringComparison.Ordinal))
                throw new AdoException("conflicting_pr_branch", "--branch conflicts with the PR merge ref selected by --pr-number.", ExitCode.Usage);
        }
        foreach (var value in new[] { RepositoryId, RepositoryType })
            if (value is not null && (string.IsNullOrWhiteSpace(value) || value.Length > 1024 || value.Any(char.IsControl)))
                throw new AdoException("invalid_repository_filter", "Repository filters must be nonempty, at most 1024 characters and contain no control characters.", ExitCode.Usage);
        if (SourceSha is not null && (SourceSha.Length != 40 || !SourceSha.All(Uri.IsHexDigit)))
            throw new AdoException("invalid_source_sha", "--source-sha requires a full 40-character Git SHA.", ExitCode.Usage);
        if (DefinitionId is <= 0)
            throw new AdoException("invalid_definition", "--definition-id must be positive.", ExitCode.Usage);
        if (Status is not (null or "none" or "inProgress" or "completed" or "cancelling" or "postponed" or "notStarted" or "all"))
            throw new AdoException("invalid_build_status", "Use a documented Build status: none, inProgress, completed, cancelling, postponed, notStarted or all.", ExitCode.Usage);
        if (Result is not (null or "none" or "succeeded" or "partiallySucceeded" or "failed" or "canceled"))
            throw new AdoException("invalid_build_result", "Use a documented Build result: none, succeeded, partiallySucceeded, failed or canceled.", ExitCode.Usage);
        if (Branch is not null && (string.IsNullOrWhiteSpace(Branch) || Branch.Length > 1024 || Branch.Any(char.IsControl)))
            throw new AdoException("invalid_branch", "--branch must be nonempty, at most 1024 characters and contain no control characters.", ExitCode.Usage);
    }
}
