using Ado.Domain;
using Ado.Infrastructure;

namespace Ado.Cli;

internal static class BuildTimelineCommands
{
    public static async Task<int> ReadAsync(BuildTimelineClient client, ServiceOptions options, int limit,
        TextWriter output, TextWriter error, CancellationToken cancellationToken)
    {
        var result = await client.GetAsync(options.BuildId!.Value, limit, cancellationToken, options.IncludeHistory);
        bool incomplete = result.Meta.Completeness != "complete";
        if (options.Json)
        {
            if (options.RequireComplete && incomplete) await OutputWriter.PartialAsync(output, result.Items, result.Meta);
            else await OutputWriter.SuccessAsync(output, result.Items, true, result.Meta);
        }
        else
        {
            var records = result.Items.ToDictionary(item => (item.TimelineId, item.Id));
            await output.WriteLineAsync((options.IncludeHistory ? "TIMELINE ID  " : "") + "ORDER  TYPE  NAME  PARENT  ATTEMPT  PREVIOUS  STATE  RESULT  LOG ID  ERRORS  WARNINGS");
            foreach (var item in result.Items)
            {
                string parent = item.ParentId is not { } parentId ? "" : records.TryGetValue((item.TimelineId, parentId), out var parentRecord)
                    ? parentRecord.Name ?? parentId.ToString() : "unavailable:" + parentId;
                await output.WriteLineAsync((options.IncludeHistory ? $"{item.TimelineId?.ToString() ?? "unknown"}  " : "") + $"{item.Order}  {OutputWriter.TerminalSafe(item.Type ?? "")}  {OutputWriter.TerminalSafe(item.Name ?? "")}  {OutputWriter.TerminalSafe(parent)}  {item.Attempt?.ToString() ?? "unknown"}  {item.PreviousAttempts?.Count.ToString() ?? "unknown"}  {OutputWriter.TerminalSafe(item.State ?? "")}  {OutputWriter.TerminalSafe(item.Result ?? "")}  {item.LogId}  {item.ErrorCount}  {item.WarningCount}");
            }
            if (incomplete) await error.WriteLineAsync("warning: Timeline results are limited or have unloaded hierarchy/attempt context; inspect JSON metadata.");
        }
        return options.RequireComplete && incomplete ? (int)ExitCode.Partial : 0;
    }
}
