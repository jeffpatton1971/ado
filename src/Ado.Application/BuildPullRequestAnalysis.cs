using System.Globalization;
using Ado.Domain;

namespace Ado.Application;

public static class BuildPullRequestAnalysis
{
    public static BuildPullRequestContext? Analyze(string? reason, string? branch, string? version, string? repositoryType)
    {
        bool prReason = reason == "pullRequest";
        bool supportedGit = string.Equals(repositoryType, "GitHub", StringComparison.OrdinalIgnoreCase)
            || string.Equals(repositoryType, "TfsGit", StringComparison.OrdinalIgnoreCase);
        int? number = null;
        if (supportedGit && branch is { Length: > 16 } && branch.StartsWith("refs/pull/", StringComparison.Ordinal)
            && branch.EndsWith("/merge", StringComparison.Ordinal))
        {
            string text = branch[10..^6];
            if (int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out int parsed) && parsed > 0
                && text == parsed.ToString(CultureInfo.InvariantCulture)) number = parsed;
        }
        if (!prReason && number is null) return null;
        string evidence = number is null ? "pull_request_reason_only"
            : prReason ? "pull_request_reason_and_merge_ref" : "merge_ref_only";
        string kind = prReason && number is not null && !string.IsNullOrWhiteSpace(version)
            ? "reported_merge_commit" : "unknown";
        // Neither a PR number/ref nor the built merge revision establishes the historical PR head.
        return new(number, evidence, kind, null, "not_resolved");
    }
}
