using Ado.Domain;
using Ado.Infrastructure;

namespace Ado.Cli;

internal static class BuildTimelineCommands
{
    public static async Task<int> ReadAsync(BuildTimelineClient client, ServiceOptions options, int limit,
        TextWriter output, TextWriter error, CancellationToken cancellationToken)
    {
        var result = await client.GetAsync(options.BuildId!.Value, limit, cancellationToken);
        bool incomplete = result.Meta.Completeness != "complete";
        if (options.Json)
        {
            if (options.RequireComplete && incomplete) await OutputWriter.PartialAsync(output, result.Items, result.Meta);
            else await OutputWriter.SuccessAsync(output, result.Items, true, result.Meta);
        }
        else
        {
            await output.WriteLineAsync("TYPE  NAME  STATE  RESULT  LOG ID  ERRORS  WARNINGS");
            foreach (var item in result.Items)
                await output.WriteLineAsync($"{OutputWriter.TerminalSafe(item.Type ?? "")}  {OutputWriter.TerminalSafe(item.Name ?? "")}  {OutputWriter.TerminalSafe(item.State ?? "")}  {OutputWriter.TerminalSafe(item.Result ?? "")}  {item.LogId}  {item.ErrorCount}  {item.WarningCount}");
            if (incomplete) await error.WriteLineAsync("warning: Timeline results are limited or contain unloaded sub-timelines; inspect JSON metadata.");
        }
        return options.RequireComplete && incomplete ? (int)ExitCode.Partial : 0;
    }
}
