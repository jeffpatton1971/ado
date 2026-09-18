using Ado.Domain;
using Ado.Infrastructure;

namespace Ado.Cli;

internal static class ReleaseCommands
{
    public static async Task<int> ReadAsync(ReleasesClient client, ServiceOptions options, int top, int limit,
        TextWriter output, TextWriter error, CancellationToken cancellationToken)
    {
        if (options.Command == "release environments")
            return await EnvironmentsAsync(client, options, limit, output, error, cancellationToken);
        if (options.Command == "release approvals")
            return await ApprovalsAsync(client, options, limit, output, error, cancellationToken);
        if (options.Command == "release deployments")
            return await DeploymentsAsync(client, options, limit, output, error, cancellationToken);
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

    private static async Task<int> EnvironmentsAsync(ReleasesClient client, ServiceOptions options, int limit,
        TextWriter output, TextWriter error, CancellationToken cancellationToken)
    {
        var result = await client.EnvironmentsAsync(options.ReleaseId!.Value, limit, cancellationToken);
        bool incomplete = result.Meta.Completeness != "complete";
        if (options.Json)
        {
            if (options.RequireComplete && incomplete) await OutputWriter.PartialAsync(output, result.Items, result.Meta);
            else await OutputWriter.SuccessAsync(output, result.Items, true, result.Meta);
        }
        else
        {
            await output.WriteLineAsync("ENVIRONMENT ID  RELEASE ID  DEFINITION ENVIRONMENT ID  NAME  STATUS  RANK");
            foreach (var item in result.Items)
                await output.WriteLineAsync($"{item.Id}  {item.ReleaseId}  {item.DefinitionEnvironmentId}  {OutputWriter.TerminalSafe(item.Name ?? "")}  {OutputWriter.TerminalSafe(item.Status ?? "")}  {item.Rank}");
            if (incomplete) await error.WriteLineAsync("warning: Environments are truncated; raise --limit within its ceiling. There is no continuation token.");
        }
        return options.RequireComplete && incomplete ? (int)ExitCode.Partial : 0;
    }

    private static async Task<int> DeploymentsAsync(ReleasesClient client, ServiceOptions options, int limit,
        TextWriter output, TextWriter error, CancellationToken cancellationToken)
    {
        var result = await client.DeploymentsAsync(options.ReleaseId!.Value, limit, cancellationToken);
        bool incomplete = result.Meta.Completeness != "complete";
        if (options.Json)
        {
            if (options.RequireComplete && incomplete) await OutputWriter.PartialAsync(output, result.Items, result.Meta);
            else await OutputWriter.SuccessAsync(output, result.Items, true, result.Meta);
        }
        else
        {
            await output.WriteLineAsync("STEP ID  DEPLOYMENT ID  ENVIRONMENT ID  ENVIRONMENT  ATTEMPT  STATUS  OPERATION STATUS  STARTED");
            foreach (var item in result.Items)
                await output.WriteLineAsync($"{item.Id}  {item.DeploymentId}  {item.EnvironmentId}  {OutputWriter.TerminalSafe(item.EnvironmentName ?? "")}  {item.Attempt}  {OutputWriter.TerminalSafe(item.Status ?? "")}  {OutputWriter.TerminalSafe(item.OperationStatus ?? "")}  {item.HasStarted}");
            if (incomplete) await error.WriteLineAsync("warning: Deployment attempts are truncated or arrays are missing; inspect JSON metadata. There is no continuation token.");
        }
        return options.RequireComplete && incomplete ? (int)ExitCode.Partial : 0;
    }

    private static async Task<int> ApprovalsAsync(ReleasesClient client, ServiceOptions options, int limit,
        TextWriter output, TextWriter error, CancellationToken cancellationToken)
    {
        var result = await client.ApprovalsAsync(options.ReleaseId!.Value, limit, cancellationToken);
        bool incomplete = result.Meta.Completeness != "complete";
        if (options.Json)
        {
            if (options.RequireComplete && incomplete) await OutputWriter.PartialAsync(output, result.Items, result.Meta);
            else await OutputWriter.SuccessAsync(output, result.Items, true, result.Meta);
        }
        else
        {
            await output.WriteLineAsync("APPROVAL ID  ENVIRONMENT ID  ENVIRONMENT  PHASE  STATUS  AUTOMATED  ATTEMPT  RANK");
            foreach (var item in result.Items)
                await output.WriteLineAsync($"{item.Id}  {item.EnvironmentId}  {OutputWriter.TerminalSafe(item.EnvironmentName ?? "")}  {item.Phase}  {OutputWriter.TerminalSafe(item.Status ?? "")}  {item.IsAutomated}  {item.Attempt}  {item.Rank}");
            if (incomplete) await error.WriteLineAsync("warning: Approvals are truncated or approval arrays are missing; inspect JSON metadata. There is no continuation token.");
        }
        return options.RequireComplete && incomplete ? (int)ExitCode.Partial : 0;
    }
}
