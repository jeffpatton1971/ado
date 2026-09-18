using Ado.Domain;
using Ado.Infrastructure;

namespace Ado.Cli;

internal static class BuildLogCommands
{
    public static async Task<int> ReadAsync(BuildLogsClient client, ServiceOptions options, int limit,
        TextWriter output, TextWriter error, CancellationToken cancellationToken)
    {
        object data;
        ResultMetadata meta;
        if (options.Command == "build logs")
        {
            var result = await client.ListAsync(options.BuildId!.Value, limit, cancellationToken);
            data = result.Items;
            meta = result.Meta;
            if (!options.Json)
            {
                await output.WriteLineAsync("LOG ID  BUILD ID  LINES  TYPE");
                foreach (var item in result.Items)
                    await output.WriteLineAsync($"{item.Id}  {item.BuildId}  {item.LineCount?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "unknown"}  {OutputWriter.TerminalSafe(item.Type ?? "")}");
            }
        }
        else
        {
            var result = await client.GetAsync(options.BuildId!.Value, options.LogId!.Value, options.StartLine, options.EndLine, limit, cancellationToken);
            data = result.Data;
            meta = result.Meta;
            if (!options.Json)
                foreach (string line in result.Data.Lines) await output.WriteLineAsync(OutputWriter.TerminalSafe(line));
        }
        bool incomplete = meta.Completeness != "complete";
        if (options.Json)
        {
            if (options.RequireComplete && incomplete) await OutputWriter.PartialValueAsync(output, data, meta);
            else await OutputWriter.SuccessAsync(output, data, true, meta);
        }
        else if (incomplete)
            await error.WriteLineAsync(meta.Truncated
                ? "warning: Log output is truncated by the selected limit. Raise --limit within its ceiling; this endpoint has no continuation token."
                : "warning: A line range was requested; completeness of the full log is unknown.");
        return options.RequireComplete && incomplete ? (int)ExitCode.Partial : 0;
    }
}
