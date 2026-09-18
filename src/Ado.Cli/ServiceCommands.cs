using Ado.Application;
using Ado.Domain;
using Ado.Infrastructure;
using Ado.Infrastructure.Http;
using Ado.Platform;

namespace Ado.Cli;

public sealed record ServiceOptions(string Command, bool Json, bool NonInteractive, bool ReadOnly, bool DryRun,
    int? Top, bool All, string? Continuation, bool RequireComplete, string? Search, int? PipelineId = null, int? RunId = null,
    string? Confirmation = null, string? RefName = null, string? ParametersFile = null, string? VariablesFile = null, bool ShowYaml = false,
    int? BuildId = null, BuildFilters? BuildFilters = null, int? LogId = null, long? StartLine = null, long? EndLine = null, string? ArtifactName = null,
    string? Destination = null, long? MaxBytes = null, int? DownloadTimeout = null, int? ReleaseId = null, int? ReleaseDefinitionId = null,
    int? EnvironmentId = null, int? DeploymentId = null, int? TaskId = null, bool IncludeHistory = false, string? ArchiveEntry = null);

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
        bool buildCommand = options.Command.StartsWith("build ", StringComparison.Ordinal);
        if (options.Command == "release task log")
        {
            _ = EndpointBuilder.Release(Operations.ReleaseGet, organization, profile.Project!, options.ReleaseId);
            if (options.EnvironmentId is null or <= 0 || options.DeploymentId is null or <= 0 || options.TaskId is null or <= 0
                || options.StartLine is < 0 || options.EndLine is < 0 || (options.StartLine is not null && options.EndLine < options.StartLine))
                throw new AdoException("invalid_log_context", "Supply positive --environment-id, --deployment-id and --task-id and valid nonnegative line bounds.", ExitCode.Usage);
        }
        if (options.Command is "release list" or "release get" or "release environments" or "release approvals" or "release deployments" or "release tasks")
            _ = options.Command != "release list"
                ? EndpointBuilder.Release(Operations.ReleaseGet, organization, profile.Project!, options.ReleaseId)
                : EndpointBuilder.Release(Operations.ReleaseList, organization, profile.Project!, top: top, continuation: options.Continuation, definitionId: options.ReleaseDefinitionId);
        if (buildCommand)
        {
            EndpointBuilder.ProjectSegment(profile.Project);
            (options.BuildFilters ?? new()).Validate();
            if (options.Command != "build list" && options.BuildId is null or <= 0)
                throw new AdoException("build_required", "Supply a positive --build-id.", ExitCode.Usage);
            if (options.Command is "build logs" or "build log get")
                _ = EndpointBuilder.BuildLog(options.Command == "build logs" ? Operations.BuildLogs : Operations.BuildLogGet,
                    organization, profile.Project!, options.BuildId!.Value, options.LogId, options.StartLine, options.EndLine);
            if (options.Command is "build artifact list" or "build artifact get" or "build artifact download" or "build artifact evidence")
                _ = EndpointBuilder.BuildArtifact(options.Command == "build artifact list" ? Operations.BuildArtifactList : Operations.BuildArtifactGet,
                    organization, profile.Project!, options.BuildId!.Value, options.ArtifactName);
            // Validate pagination before credential acquisition as well as at endpoint construction.
            if (options.Command == "build list")
                _ = EndpointBuilder.Build(Operations.BuildList, organization, profile.Project!, top: top, continuation: options.Continuation, filters: options.BuildFilters);
        }
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
            DownloadTarget? downloadTarget = null;
            DownloadTarget? evidenceTarget = null;
            long maxBytes = options.MaxBytes ?? profile.Downloads.MaxBytes;
            int downloadSeconds = options.DownloadTimeout ?? profile.Downloads.TimeoutSeconds;
            if (options.Command == "build artifact evidence")
            {
                if (string.IsNullOrEmpty(options.ArchiveEntry) || options.ArchiveEntry.Length > 2048 || options.ArchiveEntry.EndsWith('/') || options.ArchiveEntry.Any(char.IsControl))
                    throw new AdoException("archive_entry_required", "Evidence export requires one exact file --entry.", ExitCode.Usage);
                if (maxBytes <= 0 || maxBytes > profile.Downloads.MaxBytes || downloadSeconds <= 0 || downloadSeconds > profile.Downloads.TimeoutSeconds)
                    throw new AdoException("invalid_download_bounds", "Download overrides must be positive and within configured ceilings.", ExitCode.Usage);
                maxBytes = Math.Min(maxBytes, 64 * 1024 * 1024);
                evidenceTarget = DownloadTarget.Validate(options.Destination);
                if (options.DryRun)
                {
                    await OutputWriter.SuccessAsync(output, new
                    {
                        action = options.Command,
                        dryRun = true,
                        written = false,
                        organization,
                        project = profile.Project,
                        buildId = options.BuildId,
                        artifactName = options.ArtifactName,
                        entry = options.ArchiveEntry,
                        destination = evidenceTarget.Path,
                        maxBytes,
                        note = "Local plan only; no credentials, HTTP requests or writes. Metadata and content are not yet verified."
                    }, options.Json);
                    return 0;
                }
            }
            if (options.Command == "build artifact download")
            {
                if (maxBytes <= 0 || maxBytes > profile.Downloads.MaxBytes || downloadSeconds <= 0 || downloadSeconds > profile.Downloads.TimeoutSeconds)
                    throw new AdoException("invalid_download_bounds", "Download overrides must be positive and within the configured ceilings.", ExitCode.Usage);
                downloadTarget = DownloadTarget.Validate(options.Destination);
                if (options.DryRun)
                {
                    await OutputWriter.SuccessAsync(output, new
                    {
                        action = options.Command,
                        dryRun = true,
                        downloaded = false,
                        buildId = options.BuildId,
                        artifactName = options.ArtifactName,
                        destination = downloadTarget.Path,
                        maxBytes,
                        timeoutSeconds = Math.Min(downloadSeconds, profile.Timeouts.OperationSeconds),
                        format = "zip",
                        note = "Local plan only. No credentials, HTTP requests or file writes; remote resource support is not verified."
                    }, options.Json,
                        new(Organization: organization, Project: profile.Project));
                    return 0;
                }
            }
            PipelineRunRequest? runRequest = null;
            bool serverPreview = options.Command == "pipeline run preview";
            if (options.Command == "pipeline run start" || serverPreview)
            {
                var operation = serverPreview ? Operations.PipelineRunPreview : Operations.PipelineRunStart;
                if (!options.DryRun) SafetyPolicy.BeforeDispatch(operation, options.ReadOnly, false);
                runRequest = await PipelineRunRequest.LoadAsync(options.ParametersFile, options.VariablesFile, options.RefName, deadline.Token);
                if (serverPreview) runRequest.ValidatePreview();
                string confirmation = serverPreview ? MutationConfirmation.PipelinePreviewTarget(organization, profile.Project!, options.PipelineId!.Value)
                    : MutationConfirmation.PipelineStartTarget(organization, profile.Project!, options.PipelineId!.Value);
                if (options.DryRun)
                {
                    await OutputWriter.SuccessAsync(output, new
                    {
                        action = options.Command,
                        dryRun = true,
                        submitted = false,
                        previewRun = serverPreview,
                        organization,
                        project = profile.Project,
                        pipelineId = options.PipelineId,
                        method = "POST",
                        apiVersion = operation.ApiVersion,
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
                if (serverPreview && options.ShowYaml)
                    await error.WriteLineAsync("warning: Expanded YAML may contain repository secrets or transformed input values that cannot be reliably redacted. Treat --show-yaml output as sensitive.");
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
            if (evidenceTarget is not null)
            {
                using var downloadService = testHandler is null ? BuildArtifactDownloader.CreateClient() : new HttpClient(testHandler, false) { Timeout = Timeout.InfiniteTimeSpan };
                using var contentClient = testHandler is null ? BuildArtifactDownloader.CreateClient() : new HttpClient(testHandler, false) { Timeout = Timeout.InfiniteTimeSpan };
                var downloader = new BuildArtifactDownloader(downloadService, contentClient, authentication, organization, profile.Project!);
                var evidenceResult = await new BuildArtifactEvidence(transport, downloader, organization, profile.Project!).ExportAsync(options.BuildId!.Value,
                    options.ArtifactName!, options.ArchiveEntry!, evidenceTarget, maxBytes, downloadSeconds, deadline.Token);
                await OutputWriter.SuccessAsync(output, evidenceResult, options.Json, new(Organization: organization, Project: profile.Project));
                return 0;
            }
            if (options.Command == "release task log")
                return await ReleaseCommands.TaskLogAsync(new ReleasesClient(transport, organization, profile.Project!), options, limit, output, error, deadline.Token);
            if (options.Command is "release list" or "release get" or "release environments" or "release approvals" or "release deployments" or "release tasks")
                return await ReleaseCommands.ReadAsync(new ReleasesClient(transport, organization, profile.Project!), options, top, limit, output, error, deadline.Token);
            if (downloadTarget is not null)
            {
                var metadata = await new BuildArtifactsClient(transport, organization, profile.Project!).GetAsync(options.BuildId!.Value, options.ArtifactName!, deadline.Token);
                if (metadata.Items[0].ResourceType is not ("Container" or "PipelineArtifact"))
                    throw new AdoException("unsupported_artifact_type", "ZIP download is currently supported only for Container and PipelineArtifact build outputs.", ExitCode.Safety);
                Uri? signedContent = metadata.Items[0].ResourceType == "PipelineArtifact"
                    ? await new BuildArtifactsClient(transport, organization, profile.Project!).GetSignedContentAsync(options.BuildId.Value, options.ArtifactName!, deadline.Token)
                    : null;
                using var downloadService = testHandler is null ? BuildArtifactDownloader.CreateClient() : new HttpClient(testHandler, false) { Timeout = Timeout.InfiniteTimeSpan };
                using var contentClient = testHandler is null ? BuildArtifactDownloader.CreateClient() : new HttpClient(testHandler, false) { Timeout = Timeout.InfiniteTimeSpan };
                var downloadResult = await new BuildArtifactDownloader(downloadService, contentClient, authentication, organization, profile.Project!)
                    .DownloadAsync(options.BuildId.Value, options.ArtifactName!, downloadTarget, maxBytes, downloadSeconds, deadline.Token, signedContent);
                await OutputWriter.SuccessAsync(output, downloadResult, options.Json, new(Organization: organization, Project: profile.Project));
                return 0;
            }
            if (options.Command is "build artifact list" or "build artifact get")
                return await BuildArtifactCommands.ReadAsync(new BuildArtifactsClient(transport, organization, profile.Project!), options, limit, output, error, deadline.Token);
            if (options.Command == "build timeline")
                return await BuildTimelineCommands.ReadAsync(new BuildTimelineClient(transport, organization, profile.Project!), options, limit, output, error, deadline.Token);
            if (options.Command == "build diagnose")
                return await BuildDiagnosisCommands.ReadAsync(new BuildsClient(transport, organization, profile.Project!),
                    new BuildTimelineClient(transport, organization, profile.Project!), options, limit, output, error, deadline.Token);
            if (options.Command is "build logs" or "build log get")
                return await BuildLogCommands.ReadAsync(new BuildLogsClient(transport, organization, profile.Project!), options, limit, output, error, deadline.Token);
            if (buildCommand)
                return await BuildCommands.ReadAsync(new BuildsClient(transport, organization, profile.Project!), options, top, limit, output, error, deadline.Token);
            if (runRequest is not null)
            {
                if (serverPreview)
                {
                    var preview = await new PipelinesClient(transport, organization, profile.Project!).PreviewAsync(options.PipelineId!.Value, runRequest, options.Confirmation, options.ShowYaml, deadline.Token);
                    await OutputWriter.SuccessAsync(output, preview, options.Json, new(Organization: organization, Project: profile.Project));
                    return 0;
                }
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
