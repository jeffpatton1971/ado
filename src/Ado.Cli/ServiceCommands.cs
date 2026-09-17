using Ado.Application;
using Ado.Domain;
using Ado.Infrastructure;
using Ado.Infrastructure.Http;
using Ado.Platform;

namespace Ado.Cli;

public sealed record ServiceOptions(string Command, bool Json, bool NonInteractive, bool ReadOnly, bool DryRun,
    int? Top, bool All, string? Continuation, bool RequireComplete, string? Search, int? PipelineId = null, int? RunId = null,
    string? Confirmation = null, string? RefName = null, string? ParametersFile = null, string? VariablesFile = null);

internal static class ServiceCommands
{
    public static async Task<int> RunAsync(Profile profile, CredentialSelection selection, SecretValue? argumentToken,
        ServiceOptions options, TextWriter output, TextWriter error, TextReader input, Func<string, string?> environment,
        HttpMessageHandler? testHandler, ICredentialProvider? testNativeProvider, CancellationToken cancellationToken)
    {
        if (options.Command == "doctor")
        {
            // Local diagnostics never retrieve secrets or access the network.
            await OutputWriter.SuccessAsync(output, new
            {
                version = CliApp.Version,
                platform = System.Runtime.InteropServices.RuntimeInformation.OSDescription,
                architecture = System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture.ToString(),
                organizationConfigured = !string.IsNullOrWhiteSpace(profile.Organization),
                credentialProvider = selection.Reference.Provider,
                checks = new[] { "configuration_valid" },
                credentialAccess = "not_tested",
                networkAccess = "not_tested",
                nextStep = "Use auth check with explicit organization/project to test endpoint access."
            }, options.Json);
            return 0;
        }
        string organization = EndpointBuilder.ValidateOrganization(profile.Organization);
        int limit = options.All ? profile.Pagination.MaxItems : profile.Pagination.Limit;
        int top = options.Top ?? profile.Pagination.PageSize;
        if (top < 1 || top > profile.Pagination.MaxItems)
            throw new AdoException("invalid_pagination", "--top must be positive and within the configured item ceiling.", ExitCode.Usage);
        if (options.Command == "project get" && string.IsNullOrWhiteSpace(profile.Project))
            throw new AdoException("project_required", "Project get requires --project or a configured project.", ExitCode.Usage);
        if (options.Command == "project search" && string.IsNullOrWhiteSpace(options.Search))
            throw new AdoException("search_required", "Project search requires --name with a nonempty name fragment.", ExitCode.Usage);
        bool pipelineCommand = options.Command.StartsWith("pipeline ", StringComparison.Ordinal);
        if (pipelineCommand)
        {
            EndpointBuilder.ProjectSegment(profile.Project);
            if (options.Command != "pipeline list" && options.PipelineId is null or <= 0)
                throw new AdoException("pipeline_required", "Supply a positive --pipeline-id.", ExitCode.Usage);
            if (options.Command == "pipeline run get" && options.RunId is null or <= 0)
                throw new AdoException("run_required", "Supply a positive --run-id.", ExitCode.Usage);
        }
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(profile.Timeouts.OperationSeconds));
        try
        {
            PipelineRunRequest? runRequest = null;
            if (options.Command == "pipeline run start")
            {
                if (!options.DryRun) SafetyPolicy.BeforeDispatch(Operations.PipelineRunStart, options.ReadOnly, false);
                runRequest = await PipelineRunRequest.LoadAsync(options.ParametersFile, options.VariablesFile, options.RefName, deadline.Token);
                string confirmation = MutationConfirmation.PipelineStartTarget(organization, profile.Project!, options.PipelineId!.Value);
                if (options.DryRun)
                {
                    await OutputWriter.SuccessAsync(output, new
                    {
                        action = "pipeline run start",
                        dryRun = true,
                        submitted = false,
                        organization,
                        project = profile.Project,
                        pipelineId = options.PipelineId,
                        method = "POST",
                        apiVersion = Operations.PipelineRunStart.ApiVersion,
                        refName = runRequest.RefName,
                        parameterCount = runRequest.ParameterCount,
                        variableCount = runRequest.VariableCount,
                        inputValues = "omitted",
                        requiredConfirmation = confirmation,
                        note = "Local preview only; does not validate pipeline existence, permissions or YAML. No credential lookup or HTTP request."
                    }, options.Json, new(Organization: organization, Project: profile.Project));
                    return 0;
                }
                MutationConfirmation.Require(confirmation, options.Confirmation);
            }
            var reference = selection.Reference;
            bool nonInteractive = options.NonInteractive || options.Json || Console.IsInputRedirected;
            ICredentialProvider provider = reference.Provider is "environment" or "stdin" or "prompt"
                ? new InputCredentialProvider(environment, input, ct => InputCredentialProvider.PromptAsync(error, ct))
                : testNativeProvider ?? NativeCredentialProvider.CreateDefault();
            using var secret = argumentToken ?? await provider.GetAsync(reference, nonInteractive, deadline.Token);
            using var authentication = new TokenAuthentication(reference.Type, secret);
            using var client = testHandler is null ? ServiceTransport.CreateClient() : new HttpClient(testHandler, disposeHandler: false) { Timeout = Timeout.InfiniteTimeSpan };
            var transport = new ServiceTransport(client, authentication, organization, profile.Timeouts.RequestSeconds, options.ReadOnly, options.DryRun);
            if (runRequest is not null)
            {
                int runId = await new PipelinesClient(transport, organization, profile.Project!).StartAsync(options.PipelineId!.Value, runRequest, options.Confirmation, deadline.Token);
                await OutputWriter.SuccessAsync(output, new { id = runId, pipelineId = options.PipelineId, submitted = true }, options.Json,
                    new(Organization: organization, Project: profile.Project));
                return 0;
            }
            if (pipelineCommand)
                return await PipelineCommands.ReadAsync(new PipelinesClient(transport, organization, profile.Project!), options, top, limit, output, error, deadline.Token);
            var projects = new ProjectsClient(transport, organization);
            ProjectResult result;
            if (options.Command == "project get" || (options.Command == "auth check" && profile.Project is not null))
                result = await projects.GetAsync(profile.Project!, deadline.Token);
            else
                result = await projects.ListAsync(options.Command == "auth check" ? 1 : top,
                    options.Command == "auth check" ? 1 : limit, options.Continuation,
                    options.Command == "project search" ? options.Search : null, deadline.Token);

            if (options.RequireComplete && options.Command != "auth check" && result.Meta.Truncated && options.Json)
            {
                await OutputWriter.PartialAsync(output, result.Projects, result.Meta);
                return (int)ExitCode.Partial;
            }
            if (options.Command == "auth check")
            {
                await OutputWriter.SuccessAsync(output, new
                {
                    accessConfirmed = true,
                    authenticationType = reference.Type,
                    checkedCapability = profile.Project is null ? "project list" : "project get",
                    credentialValidity = "not_independently_verified",
                    note = "A successful endpoint read does not prove credential validity for public resources or permissions for other services."
                }, options.Json, result.Meta with { Truncated = false, ContinuationToken = null, Completeness = "complete", TruncationReason = null });
            }
            else if (options.Json)
                await OutputWriter.SuccessAsync(output, options.Command == "project get" ? (object)result.Projects[0] : result.Projects, true, result.Meta);
            else
            {
                await output.WriteLineAsync("ID                                    NAME  STATE");
                foreach (var item in result.Projects)
                    await output.WriteLineAsync($"{item.Id}  {OutputWriter.TerminalSafe(item.Name)}  {OutputWriter.TerminalSafe(item.State ?? "")}");
                if (result.Meta.Truncated) await error.WriteLineAsync("warning: Results are truncated; use the JSON continuation token or raise the bounded limit.");
            }
            return options.RequireComplete && options.Command != "auth check" && result.Meta.Truncated ? (int)ExitCode.Partial : 0;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        { throw new AdoException("operation_timeout", "The operation exceeded its configured deadline.", ExitCode.Transient, true); }
    }
}
