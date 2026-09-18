using Ado.Domain;
using Ado.Infrastructure;

namespace Ado.Cli;

internal static class BuildArtifactCommands
{
    public static async Task<int> ReadAsync(BuildArtifactsClient client, ServiceOptions options, int limit,
        TextWriter output, TextWriter error, CancellationToken cancellationToken)
    {
        bool single = options.Command == "build artifact get";
        var result = single
            ? await client.GetAsync(options.BuildId!.Value, options.ArtifactName!, cancellationToken)
            : await client.ListAsync(options.BuildId!.Value, limit, cancellationToken);
        bool incomplete = result.Meta.Completeness != "complete";
        if (options.Json)
        {
            if (options.RequireComplete && incomplete) await OutputWriter.PartialAsync(output, result.Items, result.Meta);
            else await OutputWriter.SuccessAsync(output, single ? (object)result.Items[0] : result.Items, true, result.Meta);
        }
        else
        {
            await output.WriteLineAsync("OUTPUT ID  BUILD ID  NAME  RESOURCE TYPE");
            foreach (var item in result.Items)
                await output.WriteLineAsync($"{item.Id}  {item.BuildId}  {OutputWriter.TerminalSafe(item.Name)}  {OutputWriter.TerminalSafe(item.ResourceType)}");
            if (incomplete) await error.WriteLineAsync("warning: Build-output metadata is truncated. Raise --limit within its ceiling; this endpoint has no continuation token.");
        }
        return options.RequireComplete && incomplete ? (int)ExitCode.Partial : 0;
    }
}
