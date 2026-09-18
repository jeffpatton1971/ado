using Ado.Domain;

namespace Ado.Application;

public sealed record BuildFilters(int? DefinitionId = null, string? Status = null, string? Result = null, string? Branch = null)
{
    public void Validate()
    {
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
