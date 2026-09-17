using Ado.Domain;

namespace Ado.Application;

public static class MutationConfirmation
{
    public static string PipelineStartTarget(string organization, string project, int pipelineId) =>
        $"pipeline run start:{organization}/{project}/{pipelineId.ToString(System.Globalization.CultureInfo.InvariantCulture)}";

    public static void Require(string expected, string? supplied)
    {
        if (!string.Equals(expected, supplied, StringComparison.Ordinal))
            throw new AdoException("confirmation_required", "Exact target confirmation is required. Use --dry-run to inspect the request and obtain the --confirm value.", ExitCode.Safety);
    }
}
