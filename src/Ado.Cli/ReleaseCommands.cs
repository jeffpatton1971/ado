using Ado.Domain;
using Ado.Infrastructure;

namespace Ado.Cli;

internal static class ReleaseCommands
{
    public static async Task<int> ReadAsync(ReleasesClient client, ServiceOptions options, int top, int limit,
        TextWriter output, TextWriter error, CancellationToken cancellationToken)
    {
        bool single = options.Command == "release get";
        var result = single ? await client.GetAsync(options.ReleaseId!.Value, cancellationToken)
            : await client.ListAsync(top, limit, options.Continuation, options.ReleaseDefinitionId, cancellationToken);
        bool incomplete = result.Meta.Completeness != "complete";
        if (options.Json)
        {
            if (options.RequireComplete && incomplete) await OutputWriter.PartialAsync(output, result.Items, result.Meta);
            else await OutputWriter.SuccessAsync(output, single ? (object)result.Items[0] : result.Items, true, result.Meta);
        }
        else
        {
            await output.WriteLineAsync("RELEASE ID  DEFINITION ID  NAME  STATUS");
            foreach (var item in result.Items)
                await output.WriteLineAsync($"{item.Id}  {item.DefinitionId}  {OutputWriter.TerminalSafe(item.Name ?? "")}  {OutputWriter.TerminalSafe(item.Status ?? "")}");
            if (incomplete) await error.WriteLineAsync("warning: Releases are truncated; JSON includes the continuation token. Resume with the same definition filter.");
        }
        return options.RequireComplete && incomplete ? (int)ExitCode.Partial : 0;
    }
}
