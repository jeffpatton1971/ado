using Ado.Domain;
using Ado.Infrastructure;

namespace Ado.Cli;

internal static class PackageCommands
{
    public static async Task<int> ReadAsync(PackagesClient client, ServiceOptions options, int top, int limit,
        TextWriter output, TextWriter error, CancellationToken cancellationToken)
    {
        if (options.Command == "package list")
        {
            var result = await client.ListAsync(options.PackageQuery ?? new(), top, limit, options.Continuation, cancellationToken);
            return await Present(result, options, output, error, "PACKAGE ID  NAME  PROTOCOL",
                item => $"{item.Id}  {OutputWriter.TerminalSafe(item.Name)}  {OutputWriter.TerminalSafe(item.ProtocolType)}");
        }
        var versions = await client.VersionsAsync(options.PackageQuery ?? new(), limit, options.Command == "package version get", cancellationToken);
        return await Present(versions, options, output, error, "VERSION ID  VERSION  LISTED  DELETED  LATEST",
            item => $"{item.Id}  {OutputWriter.TerminalSafe(item.Version)}  {item.IsListed?.ToString() ?? "unknown"}  {item.IsDeleted?.ToString() ?? "unknown"}  {item.IsLatest?.ToString() ?? "unknown"}");
    }
    private static async Task<int> Present<T>(CollectionResult<T> result, ServiceOptions options, TextWriter output, TextWriter error, string header, Func<T, string> row)
    {
        bool partial = result.Meta.Completeness != "complete";
        if (options.Json)
        {
            if (options.RequireComplete && partial) await OutputWriter.PartialAsync(output, result.Items, result.Meta);
            else await OutputWriter.SuccessAsync(output, options.Command == "package version get" ? (object)result.Items[0]! : result.Items, true, result.Meta);
        }
        else
        {
            await output.WriteLineAsync(header);
            foreach (var item in result.Items) await output.WriteLineAsync(row(item));
            if (partial) await error.WriteLineAsync(options.Command == "package list"
                ? "warning: Package search is bounded; JSON includes a numeric continuation offset. Resume with the same scope, feed and filters."
                : "warning: Version output is truncated locally; raise the bounded limit. This endpoint has no documented paging.");
            await output.WriteLineAsync("note: Results describe visible service metadata, not package downloadability or dependency compatibility. Missing results can reflect scope, permissions or service filters.");
        }
        return options.RequireComplete && partial ? (int)ExitCode.Partial : 0;
    }
}
