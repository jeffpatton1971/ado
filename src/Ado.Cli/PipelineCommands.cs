using Ado.Domain;
using Ado.Infrastructure;

namespace Ado.Cli;

internal static class PipelineCommands
{
    public static async Task<int> ReadAsync(PipelinesClient client, ServiceOptions options, int top, int limit,
        TextWriter output, TextWriter error, CancellationToken cancellationToken)
    {
        if (options.Command is "pipeline list" or "pipeline get")
        {
            var result = options.Command == "pipeline list"
                ? await client.ListAsync(top, limit, options.Continuation, cancellationToken)
                : await client.GetAsync(options.PipelineId!.Value, cancellationToken);
            return await PresentAsync(result, options, output, error, "ID  NAME  FOLDER  TYPE",
                item => $"{item.Id}  {OutputWriter.TerminalSafe(item.Name)}  {OutputWriter.TerminalSafe(item.Folder ?? "")}  {OutputWriter.TerminalSafe(item.ConfigurationType)}");
        }
        var runs = options.Command == "pipeline runs"
            ? await client.RunsAsync(options.PipelineId!.Value, limit, cancellationToken)
            : await client.RunGetAsync(options.PipelineId!.Value, options.RunId!.Value, cancellationToken);
        return await PresentAsync(runs, options, output, error, "RUN ID  PIPELINE ID  NAME  STATE  RESULT",
            item => $"{item.Id}  {item.PipelineId}  {OutputWriter.TerminalSafe(item.Name ?? "")}  {OutputWriter.TerminalSafe(item.State ?? "")}  {OutputWriter.TerminalSafe(item.Result ?? "")}");
    }

    private static async Task<int> PresentAsync<T>(CollectionResult<T> result, ServiceOptions options, TextWriter output,
        TextWriter error, string header, Func<T, string> row)
    {
        bool incomplete = result.Meta.Completeness != "complete";
        if (options.Json)
        {
            if (options.RequireComplete && incomplete) await OutputWriter.PartialAsync(output, result.Items, result.Meta);
            else await OutputWriter.SuccessAsync(output,
                options.Command.EndsWith(" get", StringComparison.Ordinal) ? (object)result.Items[0]! : result.Items, true, result.Meta);
        }
        else
        {
            await output.WriteLineAsync(header);
            foreach (var item in result.Items) await output.WriteLineAsync(row(item));
            if (incomplete)
                await error.WriteLineAsync(result.Meta.ContinuationToken is not null
                    ? "warning: Results are truncated; JSON includes the continuation token."
                    : "warning: Results are limited or completeness is unknown. This endpoint has no continuation token; inspect JSON metadata.");
        }
        return options.RequireComplete && incomplete ? (int)ExitCode.Partial : 0;
    }
}
