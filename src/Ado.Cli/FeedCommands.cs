using Ado.Domain;
using Ado.Infrastructure;

namespace Ado.Cli;

internal static class FeedCommands
{
    public static async Task<int> ReadAsync(FeedsClient client, ServiceOptions options, int limit,
        TextWriter output, TextWriter error, CancellationToken cancellationToken)
    {
        var result = await client.ReadAsync(options.Feed, limit, cancellationToken);
        bool incomplete = result.Meta.Completeness != "complete";
        if (options.Json)
        {
            if (options.RequireComplete && incomplete) await OutputWriter.PartialAsync(output, result.Items, result.Meta);
            else await OutputWriter.SuccessAsync(output, options.Command == "feed get" ? (object)result.Items[0] : result.Items, true, result.Meta);
        }
        else
        {
            await output.WriteLineAsync("FEED ID  NAME  SCOPE  PROJECT");
            foreach (var item in result.Items)
                await output.WriteLineAsync($"{item.Id}  {OutputWriter.TerminalSafe(item.Name)}  {item.Scope}  {OutputWriter.TerminalSafe(item.ProjectName ?? item.ProjectId?.ToString() ?? "")}");
            if (incomplete) await error.WriteLineAsync("warning: Feeds are truncated by the local limit. Raise --limit or use --all within configured bounds; this endpoint has no documented continuation token.");
            await output.WriteLineAsync("note: Results reflect feeds visible to the selected credential and scope; absence does not prove a feed does not exist.");
        }
        return options.RequireComplete && incomplete ? (int)ExitCode.Partial : 0;
    }
}
