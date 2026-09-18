using System.CommandLine;
using System.Reflection;
using Ado.Application;
using Ado.Domain;
using Ado.Infrastructure.Configuration;
using Ado.Infrastructure.Http;
using Ado.Infrastructure;

namespace Ado.Cli;

public static class CliApp
{
    public static string Version => typeof(CliApp).Assembly
        .GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion.Split('+')[0];

    public static async Task<int> RunAsync(string[] args, TextWriter output, TextWriter error,
        CancellationToken cancellationToken = default, Func<string, string?>? environment = null,
        TextReader? input = null, HttpMessageHandler? testHandler = null, ICredentialProvider? testNativeProvider = null)
    {
        environment ??= Environment.GetEnvironmentVariable;
        var root = new RootCommand("Azure DevOps Services CLI. Licensed under AGPL-3.0-only; no warranty.");
        var json = new Option<bool>("--json") { Recursive = true, Description = "Emit a stable JSON envelope." };
        var format = new Option<string>("--output") { Recursive = true, Description = "table or json" };
        var configPath = new Option<string>("--config") { Recursive = true, Description = "Explicit strict JSON configuration file." };
        var profile = new Option<string>("--profile") { Recursive = true, Description = "Named profile (no inheritance)." };
        var organization = new Option<string>("--organization") { Recursive = true };
        var project = new Option<string>("--project") { Recursive = true };
        var limit = new Option<int?>("--limit") { Recursive = true };
        var timeout = new Option<int?>("--timeout") { Recursive = true, Description = "Request timeout in seconds." };
        var authType = new Option<string>("--auth-type") { Recursive = true, Description = "pat or entra-token" };
        var token = new Option<string>("--token") { Recursive = true, Description = "Insecure: token may appear in process lists/history. Prefer stdin or keyring." };
        var tokenStdin = new Option<bool>("--token-stdin") { Recursive = true };
        var tokenPrompt = new Option<bool>("--token-prompt") { Recursive = true };
        var credentialProvider = new Option<string>("--credential-provider") { Recursive = true };
        var credentialService = new Option<string>("--credential-service") { Recursive = true };
        var credentialAccount = new Option<string>("--credential-account") { Recursive = true };
        var nonInteractive = new Option<bool>("--non-interactive") { Recursive = true };
        var readOnly = new Option<bool>("--read-only") { Recursive = true };
        var dryRun = new Option<bool>("--dry-run") { Recursive = true };
        foreach (var option in new Option[] { json, format, configPath, profile, organization, project, limit, timeout,
            authType, token, tokenStdin, tokenPrompt, credentialProvider, credentialService, credentialAccount, nonInteractive, readOnly, dryRun })
            root.Options.Add(option);
        var config = new Command("config", "Inspect configuration without retrieving credentials.");
        var paths = new Command("paths", "Show resolved configuration locations.");
        var show = new Command("show", "Show validated configuration without secrets.");
        var effective = new Option<bool>("--effective") { Description = "Resolve flags, environment and the selected profile." };
        show.Options.Add(effective);
        config.Subcommands.Add(paths);
        config.Subcommands.Add(show);
        root.Subcommands.Add(config);
        var projectCommand = new Command("project", "Read Azure DevOps projects.");
        var list = new Command("list", "List accessible projects with bounded pagination.");
        var get = new Command("get", "Get the project selected by --project.");
        var search = new Command("search", "Filter a bounded project scan by name (client-side substring match).");
        var top = new Option<int?>("--top") { Description = "Requested page size." };
        var all = new Option<bool>("--all") { Description = "Scan up to the configured maximum." };
        var continuation = new Option<string>("--continuation-token") { Description = "Endpoint continuation token (numeric for projects/releases, opaque for pipelines/builds)." };
        var requireComplete = new Option<bool>("--require-complete") { Description = "Exit 10 if results are truncated or completeness is unknown." };
        var searchName = new Option<string>("--name") { Description = "Project name fragment." };
        var feedCommand = new Command("feed", "Inspect accessible Azure Artifacts feeds.");
        var feedList = new Command("list", "List accessible feeds; bounded locally, no server pagination.");
        var feedGet = new Command("get", "Get feed identity by name or ID.");
        var feedScope = new Option<string>("--scope") { Description = "project (default, uses configured project) or organization (omits project from the route)." };
        var feedSelector = new Option<string>("--feed") { Description = "Feed name or ID; views are not supported." };
        foreach (var command in new[] { feedList, feedGet }) { command.Options.Add(feedScope); feedCommand.Subcommands.Add(command); }
        foreach (var option in new Option[] { all, requireComplete }) feedList.Options.Add(option);
        feedGet.Options.Add(feedSelector);
        root.Subcommands.Add(feedCommand);
        var package = new Command("package", "Inspect Azure Artifacts package identities and versions.");
        var packageList = new Command("list", "List package identities with bounded offset pagination; no version inventory.");
        var packageVersions = new Command("versions", "List non-deleted versions of a package GUID; bounded locally.");
        var packageVersion = new Command("version", "Inspect an exact package version identity.");
        var packageVersionGet = new Command("get", "Get a version by its service GUID, not a version range.");
        var packageResolve = new Command("resolve", "Resolve an exact NuGet name and literal version to service GUIDs using bounded metadata reads.");
        var packageVersionText = new Option<string>("--package-version") { Description = "Literal display or normalized version string; no ranges, wildcards or inferred normalization." };
        var packageId = new Option<string>("--package-id") { Description = "Package GUID from package list." };
        var versionId = new Option<string>("--version-id") { Description = "Version GUID from package versions." };
        var packageProtocol = new Option<string>("--protocol") { Description = "Service protocol filter, for example NuGet." };
        var packageName = new Option<string>("--name") { Description = "Server-side package name substring filter." };
        foreach (var command in new[] { packageList, packageVersions, packageVersionGet, packageResolve })
        {
            command.Options.Add(feedScope);
            command.Options.Add(feedSelector);
        }
        foreach (var option in new Option[] { top, all, continuation, requireComplete, packageProtocol, packageName }) packageList.Options.Add(option);
        foreach (var option in new Option[] { all, requireComplete, packageId }) packageVersions.Options.Add(option);
        packageVersionGet.Options.Add(packageId);
        packageVersionGet.Options.Add(versionId);
        foreach (var option in new Option[] { packageName, packageVersionText, all, top }) packageResolve.Options.Add(option);
        packageVersion.Subcommands.Add(packageVersionGet);
        foreach (var command in new[] { packageList, packageVersions, packageVersion, packageResolve }) package.Subcommands.Add(command);
        root.Subcommands.Add(package);
        foreach (var command in new[] { list, search })
        {
            foreach (var option in new Option[] { top, all, continuation, requireComplete }) command.Options.Add(option);
        }
        search.Options.Add(searchName);
        projectCommand.Subcommands.Add(list);
        projectCommand.Subcommands.Add(get);
        projectCommand.Subcommands.Add(search);
        root.Subcommands.Add(projectCommand);
        var pipeline = new Command("pipeline", "Inspect Pipelines API definitions and runs; distinct from builds and classic releases.");
        var pipelineList = new Command("list", "List pipeline definitions and their reported configuration types.");
        var pipelineGet = new Command("get", "Inspect a pipeline definition.");
        var pipelineRuns = new Command("runs", "Inspect runs; server caps history at 10000 without paging.");
        var pipelineRun = new Command("run", "Inspect or start a Pipelines API execution.");
        var pipelineRunGet = new Command("get", "Inspect a specific pipeline run.");
        var pipelineRunStart = new Command("start", "Submit one run after exact confirmation; --dry-run is local only.");
        var pipelineRunPreview = new Command("preview", "Ask Azure DevOps to expand YAML with previewRun:true; no run is created.");
        var showYaml = new Option<bool>("--show-yaml") { Description = "Include expanded YAML; it may expose repository secrets. Known input strings and authentication are redacted." };
        pipelineRunPreview.Options.Add(showYaml);
        var confirm = new Option<string>("--confirm") { Description = "Exact target string shown by --dry-run. Required for submission." };
        var refName = new Option<string>("--ref") { Description = "Full refs/heads/... or refs/tags/... for the self repository." };
        var parametersFile = new Option<string>("--parameters-file") { Description = "JSON object of typed templateParameters; values are omitted from local plans." };
        var variablesFile = new Option<string>("--variables-file") { Description = "JSON variable map: each entry has value and optional isSecret." };
        foreach (var command in new[] { pipelineRunStart, pipelineRunPreview })
            foreach (var option in new Option[] { confirm, refName, parametersFile, variablesFile }) command.Options.Add(option);
        var pipelineId = new Option<int?>("--pipeline-id") { Description = "Positive pipeline definition ID." };
        var runId = new Option<int?>("--run-id") { Description = "Positive pipeline run ID." };
        foreach (var option in new Option[] { top, all, continuation, requireComplete }) pipelineList.Options.Add(option);
        foreach (var option in new Option[] { all, requireComplete }) pipelineRuns.Options.Add(option);
        foreach (var command in new[] { pipelineGet, pipelineRuns, pipelineRunGet, pipelineRunStart, pipelineRunPreview }) command.Options.Add(pipelineId);
        pipelineRunGet.Options.Add(runId);
        pipelineRun.Subcommands.Add(pipelineRunGet);
        pipelineRun.Subcommands.Add(pipelineRunStart);
        pipelineRun.Subcommands.Add(pipelineRunPreview);
        foreach (var command in new[] { pipelineList, pipelineGet, pipelineRuns, pipelineRun }) pipeline.Subcommands.Add(command);
        root.Subcommands.Add(pipeline);
        var build = new Command("build", "Inspect Build API execution records, separate from Pipelines runs.");
        var buildList = new Command("list", "List builds newest queued first with bounded pagination.");
        var buildGet = new Command("get", "Get a Build API record by build ID.");
        var buildLogs = new Command("logs", "List log IDs and line counts for a build; no server pagination.");
        var buildTimeline = new Command("timeline", "Inspect jobs/tasks, their results and log IDs in the default build timeline.");
        var includeHistory = new Option<bool>("--include-history") { Description = "Follow bounded sub-timeline and previous-attempt references." };
        buildTimeline.Options.Add(includeHistory);
        var buildDiagnose = new Command("diagnose", "Combine build details and timeline findings without inferring root cause or retrieving raw logs.");
        buildDiagnose.Options.Add(includeHistory);
        build.Subcommands.Add(buildDiagnose);
        var buildLog = new Command("log", "Read individual build logs.");
        var buildLogGet = new Command("get", "Read bounded log content; terminal controls are escaped.");
        var logId = new Option<int?>("--log-id") { Description = "Positive log ID returned by build logs." };
        var startLine = new Option<long?>("--start-line") { Description = "Nonnegative service line position; passed through unchanged." };
        var endLine = new Option<long?>("--end-line") { Description = "Nonnegative service end position; must not precede start-line." };
        var buildId = new Option<int?>("--build-id") { Description = "Positive Build API execution ID." };
        var definitionId = new Option<int?>("--definition-id") { Description = "Filter by one Build definition ID." };
        var buildStatus = new Option<string>("--status") { Description = "none, inProgress, completed, cancelling, postponed, notStarted or all" };
        var buildResult = new Option<string>("--result") { Description = "none, succeeded, partiallySucceeded, failed or canceled" };
        var branch = new Option<string>("--branch") { Description = "Exact source branch, typically refs/heads/main." };
        var repositoryId = new Option<string>("--repository-id") { Description = "Service repository ID filter." };
        var repositoryType = new Option<string>("--repository-type") { Description = "Service repository type filter, for example TfsGit." };
        var sourceSha = new Option<string>("--source-sha") { Description = "Exact full Git SHA; locally filters at most --limit scanned builds. Resume with the same filters and continuation token." };
        var prNumber = new Option<int?>("--pr-number") { Description = "PR validation builds on refs/pull/N/merge; requires repository-id and repository-type GitHub or TfsGit. source-sha matches the built merge commit, not PR head." };
        foreach (var option in new Option[] { top, all, continuation, requireComplete, definitionId, buildStatus, buildResult, branch, repositoryId, repositoryType, sourceSha, prNumber }) buildList.Options.Add(option);
        buildGet.Options.Add(buildId);
        foreach (var command in new[] { buildLogs, buildLogGet, buildTimeline, buildDiagnose })
            foreach (var option in new Option[] { buildId, all, requireComplete }) command.Options.Add(option);
        foreach (var option in new Option[] { logId, startLine, endLine }) buildLogGet.Options.Add(option);
        buildLog.Subcommands.Add(buildLogGet);
        build.Subcommands.Add(buildList);
        build.Subcommands.Add(buildGet);
        build.Subcommands.Add(buildLogs);
        build.Subcommands.Add(buildTimeline);
        build.Subcommands.Add(buildLog);
        var buildArtifact = new Command("artifact", "Inspect outputs produced by a build, not Azure Artifacts feed packages.");
        var buildArtifactList = new Command("list", "List bounded build-output metadata; no downloads.");
        var buildArtifactGet = new Command("get", "Get one build output by name; no downloads.");
        var buildArtifactDownload = new Command("download", "Download a ZIP to a new explicit local file; no extraction or overwrite.");
        var destination = new Option<string>("--destination") { Description = "Required new local file path; its parent directory must exist." };
        var maxBytes = new Option<long?>("--max-bytes") { Description = "Lower the configured download byte ceiling." };
        var downloadTimeout = new Option<int?>("--download-timeout") { Description = "Lower the configured download timeout in seconds." };
        foreach (var option in new Option[] { destination, maxBytes, downloadTimeout }) buildArtifactDownload.Options.Add(option);
        var artifactName = new Option<string>("--artifact-name") { Description = "Exact build-output name returned by build artifact list." };
        foreach (var command in new[] { buildArtifactList, buildArtifactGet, buildArtifactDownload }) command.Options.Add(buildId);
        foreach (var option in new Option[] { all, requireComplete }) buildArtifactList.Options.Add(option);
        buildArtifactGet.Options.Add(artifactName);
        buildArtifactDownload.Options.Add(artifactName);
        buildArtifact.Subcommands.Add(buildArtifactList);
        buildArtifact.Subcommands.Add(buildArtifactGet);
        buildArtifact.Subcommands.Add(buildArtifactDownload);
        var runUrl = new Option<string>("--run-url") { Description = "Azure DevOps build results URL; must match selected context and any --build-id." };
        foreach (var command in new[] { buildGet, buildTimeline, buildDiagnose, buildLogs, buildLogGet, buildArtifactList, buildArtifactGet, buildArtifactDownload })
            command.Options.Add(runUrl);
        build.Subcommands.Add(buildArtifact);
        root.Subcommands.Add(build);
        var artifact = new Command("artifact", "Inspect local downloaded artifact archives.");
        var artifactInspect = new Command("inspect", "List a bounded ZIP inventory or hash one exact entry; no extraction or network access.");
        var archiveFile = new Option<string>("--file") { Description = "Existing local ZIP file." };
        var archiveEntry = new Option<string>("--entry") { Description = "Exact case-sensitive archive path to inspect and hash." };
        var expectedHash = new Option<string>("--expected-sha256") { Description = "Expected SHA-256 of the archive file." };
        var showText = new Option<bool>("--show-text") { Description = "Print selected member's UTF-8 text (may contain secrets); maximum 1 MiB." };
        var textLines = new Option<int?>("--text-lines") { Description = "Text line limit from 1 to 10000; default 100; requires --show-text." };
        foreach (var option in new Option[] { archiveFile, archiveEntry, expectedHash, showText, textLines, requireComplete }) artifactInspect.Options.Add(option);
        artifact.Subcommands.Add(artifactInspect);
        var artifactExtract = new Command("extract", "Extract one exact ZIP member to an explicit new file; no overwrite or network access.");
        foreach (var option in new Option[] { archiveFile, archiveEntry, expectedHash, destination }) artifactExtract.Options.Add(option);
        artifact.Subcommands.Add(artifactExtract);
        var artifactEvidence = new Command("evidence", "Export selected member metadata and hashes to a new JSON file; optional origin labels are unverified.");
        foreach (var option in new Option[] { archiveFile, archiveEntry, expectedHash, destination, buildId, artifactName }) artifactEvidence.Options.Add(option);
        artifact.Subcommands.Add(artifactEvidence);
        var buildArtifactEvidence = new Command("evidence", "Download one build artifact and export authenticated metadata plus selected member hashes to a new JSON file.");
        foreach (var option in new Option[] { buildId, artifactName, archiveEntry, destination, maxBytes, downloadTimeout, runUrl }) buildArtifactEvidence.Options.Add(option);
        buildArtifact.Subcommands.Add(buildArtifactEvidence);
        root.Subcommands.Add(artifact);
        var release = new Command("release", "Inspect classic releases; separate from YAML pipeline runs.");
        var releaseList = new Command("list", "List classic releases newest created first.");
        var releaseGet = new Command("get", "Get safe classic release metadata by ID.");
        var releaseEnvironments = new Command("environments", "List bounded deployment statuses for a classic release's environments.");
        var releaseApprovals = new Command("approvals", "Inspect pre/post-deployment approval statuses; no approval actions.");
        var releaseDeployments = new Command("deployments", "Inspect deployment attempts embedded in a classic release; no deployment actions.");
        var releaseTasks = new Command("tasks", "Inspect expanded task results across classic release deployment attempts.");
        var releaseId = new Option<int?>("--release-id") { Description = "Positive classic release ID." };
        var releaseDefinitionId = new Option<int?>("--definition-id") { Description = "Filter by classic release definition ID." };
        foreach (var option in new Option[] { top, all, continuation, requireComplete, releaseDefinitionId }) releaseList.Options.Add(option);
        releaseGet.Options.Add(releaseId);
        foreach (var option in new Option[] { releaseId, all, requireComplete }) releaseEnvironments.Options.Add(option);
        foreach (var option in new Option[] { releaseId, all, requireComplete }) releaseApprovals.Options.Add(option);
        foreach (var option in new Option[] { releaseId, all, requireComplete }) releaseDeployments.Options.Add(option);
        foreach (var option in new Option[] { releaseId, all, requireComplete }) releaseTasks.Options.Add(option);
        release.Subcommands.Add(releaseList);
        release.Subcommands.Add(releaseGet);
        release.Subcommands.Add(releaseEnvironments);
        release.Subcommands.Add(releaseApprovals);
        release.Subcommands.Add(releaseDeployments);
        release.Subcommands.Add(releaseTasks);
        var releaseTask = new Command("task", "Inspect classic release task content.");
        var releaseTaskLog = new Command("log", "Read bounded plain-text logs for an unambiguously resolved release task.");
        var environmentId = new Option<int?>("--environment-id") { Description = "Release environment instance ID." };
        var deploymentId = new Option<int?>("--deployment-id") { Description = "Deployment ID returned by release deployments/tasks." };
        var taskId = new Option<int?>("--task-id") { Description = "Task ID returned by release tasks." };
        foreach (var option in new Option[] { releaseId, environmentId, deploymentId, taskId, startLine, endLine, all, requireComplete }) releaseTaskLog.Options.Add(option);
        releaseTask.Subcommands.Add(releaseTaskLog);
        release.Subcommands.Add(releaseTask);
        root.Subcommands.Add(release);
        var auth = new Command("auth", "Check access using the selected credential.");
        var check = new Command("check", "Test project endpoint access; other services are not checked.");
        auth.Subcommands.Add(check);
        root.Subcommands.Add(auth);
        var doctor = new Command("doctor", "Local configuration/platform diagnostics; no secret lookup or network request.");
        root.Subcommands.Add(doctor);

        var parsed = root.Parse(args);
        bool jsonOutput = parsed.GetValue(json) || (parsed.GetValue(format) ?? environment("ADO_OUTPUT")) == "json";
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (parsed.Errors.Count != 0 || parsed.GetValue(format) is not (null or "json" or "table"))
                throw new AdoException("invalid_arguments", "Invalid arguments. Use ado --help for supported syntax.", ExitCode.Usage);
            if (parsed.GetValue(token) is not null)
                await error.WriteLineAsync("warning: --token can expose the credential in process listings and shell history. Prefer --token-stdin or a credential store.");
            if (args.Contains("--version", StringComparer.Ordinal))
            {
                await output.WriteLineAsync(Version);
                return 0;
            }
            if (args.Length == 0 || parsed.Action is System.CommandLine.Help.HelpAction)
            {
                if (jsonOutput)
                    await OutputWriter.SuccessAsync(output, new { version = Version, commands = new[] { "config paths", "config show", "project list", "project get", "project search", "auth check", "doctor", "pipeline list", "pipeline get", "pipeline runs", "pipeline run get", "pipeline run start", "pipeline run preview", "build list", "build get", "build diagnose", "build timeline", "build logs", "build log get", "build artifact list", "build artifact get", "build artifact download", "build artifact evidence", "artifact inspect", "artifact extract", "artifact evidence", "release list", "release get", "release environments", "release approvals", "release deployments", "release tasks", "release task log" } }, true);
                else
                {
                    var helpArgs = args.Length == 0 ? new[] { "--help" } : args;
                    return await root.Parse(helpArgs).InvokeAsync(new InvocationConfiguration { Output = output, Error = error }, cancellationToken);
                }
                return 0;
            }

            if (parsed.CommandResult.Command == artifactEvidence)
            {
                EvidenceOrigin? origin = parsed.GetValue(organization) is not null || parsed.GetValue(project) is not null
                    || parsed.GetValue(buildId) is not null || parsed.GetValue(artifactName) is not null
                    ? new(parsed.GetValue(organization), parsed.GetValue(project), parsed.GetValue(buildId), parsed.GetValue(artifactName)) : null;
                var exported = await ArtifactEvidenceExporter.ExportAsync(parsed.GetValue(archiveFile), parsed.GetValue(archiveEntry),
                    parsed.GetValue(destination), parsed.GetValue(expectedHash), origin, parsed.GetValue(dryRun), cancellationToken);
                await OutputWriter.SuccessAsync(output, exported, jsonOutput);
                return 0;
            }
            if (parsed.CommandResult.Command == artifactExtract)
            {
                var extraction = await ArtifactArchiveInspector.ExtractAsync(parsed.GetValue(archiveFile), parsed.GetValue(archiveEntry),
                    parsed.GetValue(destination), parsed.GetValue(expectedHash), parsed.GetValue(dryRun), cancellationToken);
                await OutputWriter.SuccessAsync(output, extraction, jsonOutput);
                return 0;
            }
            if (parsed.CommandResult.Command == artifactInspect)
            {
                if (parsed.GetValue(textLines) is not null && !parsed.GetValue(showText))
                    throw new AdoException("invalid_text_options", "--text-lines requires --show-text.", ExitCode.Usage);
                var inspection = await ArtifactArchiveInspector.InspectAsync(parsed.GetValue(archiveFile), parsed.GetValue(archiveEntry),
                    parsed.GetValue(expectedHash), parsed.GetValue(limit) ?? 100, cancellationToken, parsed.GetValue(showText), parsed.GetValue(textLines) ?? 100);
                bool partial = inspection.Meta.Completeness != "complete";
                if (jsonOutput)
                {
                    if (partial && parsed.GetValue(requireComplete)) await OutputWriter.PartialValueAsync(output, inspection.Data, inspection.Meta);
                    else await OutputWriter.SuccessAsync(output, inspection.Data, true, inspection.Meta);
                }
                else
                {
                    await output.WriteLineAsync($"ARCHIVE SHA256 {inspection.Data.Sha256}  BYTES {inspection.Data.ArchiveBytes}  ENTRIES {inspection.Data.TotalEntries}");
                    await output.WriteLineAsync("PATH  TYPE  BYTES  COMPRESSED BYTES  SHA256");
                    foreach (var entry in inspection.Data.Entries)
                    {
                        await output.WriteLineAsync($"{OutputWriter.TerminalSafe(entry.Path)}  {(entry.Directory ? "directory" : "file")}  {entry.Bytes}  {entry.CompressedBytes}  {entry.Sha256 ?? "not computed"}");
                        if (entry.TextLines is not null)
                            foreach (string line in entry.TextLines) await output.WriteLineAsync(OutputWriter.TerminalSafe(line));
                    }
                    if (partial) await error.WriteLineAsync("warning: Archive inspection is truncated; inspect JSON metadata or raise --limit/--text-lines within their ceilings.");
                }
                return partial && parsed.GetValue(requireComplete) ? (int)ExitCode.Partial : 0;
            }
            var location = ConfigurationPaths.Resolve(parsed.GetValue(configPath), environment);
            if (parsed.CommandResult.Command == paths)
            {
                await OutputWriter.SuccessAsync(output, location, jsonOutput);
                return 0;
            }
            var selectedCommand = parsed.CommandResult.Command;
            string? serviceCommand = selectedCommand == packageResolve ? "package resolve"
                : selectedCommand == packageList ? "package list" : selectedCommand == packageVersions ? "package versions"
                : selectedCommand == packageVersionGet ? "package version get"
                : selectedCommand == feedList ? "feed list" : selectedCommand == feedGet ? "feed get"
                : selectedCommand == list ? "project list" : selectedCommand == get ? "project get"
                : selectedCommand == search ? "project search" : selectedCommand == check ? "auth check" : selectedCommand == doctor ? "doctor"
                : selectedCommand == pipelineList ? "pipeline list" : selectedCommand == pipelineGet ? "pipeline get"
                : selectedCommand == pipelineRuns ? "pipeline runs" : selectedCommand == pipelineRunGet ? "pipeline run get"
                : selectedCommand == pipelineRunStart ? "pipeline run start" : selectedCommand == pipelineRunPreview ? "pipeline run preview"
                : selectedCommand == buildList ? "build list" : selectedCommand == buildGet ? "build get"
                : selectedCommand == buildLogs ? "build logs" : selectedCommand == buildLogGet ? "build log get"
                : selectedCommand == buildTimeline ? "build timeline"
                : selectedCommand == buildDiagnose ? "build diagnose"
                : selectedCommand == buildArtifactList ? "build artifact list" : selectedCommand == buildArtifactGet ? "build artifact get"
                : selectedCommand == buildArtifactDownload ? "build artifact download"
                : selectedCommand == buildArtifactEvidence ? "build artifact evidence"
                : selectedCommand == releaseList ? "release list" : selectedCommand == releaseGet ? "release get"
                : selectedCommand == releaseEnvironments ? "release environments"
                : selectedCommand == releaseApprovals ? "release approvals"
                : selectedCommand == releaseDeployments ? "release deployments"
                : selectedCommand == releaseTasks ? "release tasks"
                : selectedCommand == releaseTaskLog ? "release task log" : null;
            if (selectedCommand != show && serviceCommand is null)
                throw new AdoException("command_required", "Choose a command. Use ado --help for supported syntax.", ExitCode.Usage);

            var loaded = await ConfigurationLoader.LoadAsync(location, cancellationToken);
            foreach (string warning in loaded.Warnings) await error.WriteLineAsync("warning: " + warning);
            string? profileName = parsed.GetValue(profile) ?? environment("ADO_PROFILE") ?? loaded.File.DefaultProfile;
            var configuredAuth = profileName is not null && loaded.File.Profiles.TryGetValue(profileName, out var selectedProfile)
                ? selectedProfile.Authentication : new CredentialReference();
            string? targetOrganization = parsed.GetValue(organization), targetProject = parsed.GetValue(project);
            int? targetBuildId = parsed.GetValue(buildId);
            if (parsed.GetValue(runUrl) is { } urlValue)
            {
                var target = BuildRunUrl.Parse(urlValue);
                var contextProfile = profileName is not null && loaded.File.Profiles.TryGetValue(profileName, out var foundProfile) ? foundProfile : new Profile();
                target.ValidateContext(targetOrganization ?? environment("ADO_ORGANIZATION") ?? contextProfile.Organization,
                    targetProject ?? environment("ADO_PROJECT") ?? contextProfile.Project, targetBuildId);
                targetOrganization = target.Organization;
                targetProject = target.Project;
                targetBuildId = target.BuildId;
            }
            CredentialSelection? selection = null;
            if (serviceCommand is not null && serviceCommand != "doctor" && !(serviceCommand is "pipeline run start" or "pipeline run preview" or "build artifact download" or "build artifact evidence" && parsed.GetValue(dryRun)))
                selection = CredentialSelection.Resolve(configuredAuth, parsed.GetValue(authType), parsed.GetValue(token) is not null,
                    parsed.GetValue(tokenStdin), parsed.GetValue(tokenPrompt), parsed.GetValue(credentialProvider),
                    parsed.GetValue(credentialService), parsed.GetValue(credentialAccount), environment);
            var resolved = ConfigurationResolver.Resolve(loaded.File,
                new(parsed.GetValue(profile), targetOrganization, targetProject,
                    parsed.GetValue(json) ? "json" : parsed.GetValue(format), parsed.GetValue(limit), parsed.GetValue(timeout), selection?.OverridesProfile ?? false), environment);
            jsonOutput = resolved.Settings.Output == "json";
            if (serviceCommand is not null)
            {
                if (parsed.GetValue(all) && parsed.GetValue(limit) is not null)
                    throw new AdoException("invalid_pagination", "Choose --all or --limit, not both.", ExitCode.Usage);
                using var argumentToken = parsed.GetValue(token) is { } tokenValue ? new SecretValue(tokenValue) : null;
                return await ServiceCommands.RunAsync(resolved.Settings, selection ?? new(configuredAuth, false), argumentToken,
                    new(serviceCommand, jsonOutput, parsed.GetValue(nonInteractive), parsed.GetValue(readOnly), parsed.GetValue(dryRun),
                        parsed.GetValue(top), parsed.GetValue(all), parsed.GetValue(continuation), parsed.GetValue(requireComplete), parsed.GetValue(searchName), parsed.GetValue(pipelineId), parsed.GetValue(runId),
                        parsed.GetValue(confirm), parsed.GetValue(refName), parsed.GetValue(parametersFile), parsed.GetValue(variablesFile), parsed.GetValue(showYaml),
                        targetBuildId, new(parsed.GetValue(definitionId), parsed.GetValue(buildStatus), parsed.GetValue(buildResult), parsed.GetValue(branch), parsed.GetValue(repositoryId), parsed.GetValue(repositoryType), parsed.GetValue(sourceSha), parsed.GetValue(prNumber)),
                        parsed.GetValue(logId), parsed.GetValue(startLine), parsed.GetValue(endLine), parsed.GetValue(artifactName),
                        parsed.GetValue(destination), parsed.GetValue(maxBytes), parsed.GetValue(downloadTimeout), parsed.GetValue(releaseId), parsed.GetValue(releaseDefinitionId),
                        parsed.GetValue(environmentId), parsed.GetValue(deploymentId), parsed.GetValue(taskId), parsed.GetValue(includeHistory), parsed.GetValue(archiveEntry),
                        parsed.GetValue(feedSelector), parsed.GetValue(feedScope) ?? "project",
                        new(parsed.GetValue(packageId), parsed.GetValue(versionId), parsed.GetValue(packageProtocol), parsed.GetValue(packageName), parsed.GetValue(packageVersionText))),
                    output, error, input ?? Console.In, environment, testHandler, testNativeProvider, cancellationToken);
            }
            if (parsed.GetValue(effective))
                await OutputWriter.SuccessAsync(output, resolved, jsonOutput);
            else
                await OutputWriter.SuccessAsync(output, loaded.File, jsonOutput);
            return 0;
        }
        catch (AdoException ex)
        {
            await OutputWriter.ErrorAsync(output, error, ex.Code, ex.Message, ex.Retryable, jsonOutput);
            return (int)ex.ExitCode;
        }
        catch (OperationCanceledException)
        {
            await OutputWriter.ErrorAsync(output, error, "cancelled", "Operation cancelled.", false, jsonOutput);
            return (int)ExitCode.Cancelled;
        }
        catch (Exception)
        {
            await OutputWriter.ErrorAsync(output, error, "internal_error", "An unexpected internal error occurred.", false, jsonOutput);
            return (int)ExitCode.Internal;
        }
    }
}
