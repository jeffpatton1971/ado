using Ado.Domain;
using Ado.Infrastructure;

namespace Ado.Cli;

internal static class BuildCommands
{
    public static async Task<int> ReadAsync(BuildsClient client, ServiceOptions options, int top, int limit,
        TextWriter output, TextWriter error, CancellationToken cancellationToken)
    {
        var result = options.Command == "build get"
            ? await client.GetAsync(options.BuildId!.Value, cancellationToken)
            : await client.ListAsync(top, limit, options.Continuation, options.BuildFilters ?? new(), cancellationToken);
        bool incomplete = result.Meta.Completeness != "complete";
        if (options.Json)
        {
            if (options.RequireComplete && incomplete) await OutputWriter.PartialAsync(output, result.Items, result.Meta);
            else await OutputWriter.SuccessAsync(output, options.Command == "build get" ? (object)result.Items[0] : result.Items, true, result.Meta);
        }
        else
        {
            await output.WriteLineAsync("BUILD ID  DEFINITION ID  NUMBER  STATUS  RESULT  BRANCH");
            foreach (var item in result.Items)
                await output.WriteLineAsync($"{item.Id}  {item.DefinitionId}  {OutputWriter.TerminalSafe(item.BuildNumber ?? "")}  {OutputWriter.TerminalSafe(item.Status ?? "")}  {OutputWriter.TerminalSafe(item.Result ?? "")}  {OutputWriter.TerminalSafe(item.SourceBranch ?? "")}");
            if (incomplete) await error.WriteLineAsync("warning: Results are truncated; JSON includes the continuation token. Resume with the same filters.");
        }
        return options.RequireComplete && incomplete ? (int)ExitCode.Partial : 0;
    }
}
