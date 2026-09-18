using Ado.Domain;

namespace Ado.Application;

public enum ServiceHost { Core, Release, Feeds }
public sealed record OperationDescriptor(string Command, ServiceHost Service, string ApiVersion, bool IsWrite,
    bool RequiresProject, string Confirmation, bool SupportsDryRun, string Pagination, string Scope,
    string Documentation, int OutputSchemaVersion = 1);

public static class Operations
{
    public static readonly OperationDescriptor ProjectList = new("project list", ServiceHost.Core, "7.1", false,
        false, "none", false, "header continuation / numeric offset", "vso.project",
        "https://learn.microsoft.com/en-us/rest/api/azure/devops/core/projects/list?view=azure-devops-rest-7.1");
    public static readonly OperationDescriptor ProjectGet = new("project get", ServiceHost.Core, "7.1", false,
        true, "none", false, "none", "vso.project",
        "https://learn.microsoft.com/en-us/rest/api/azure/devops/core/projects/get?view=azure-devops-rest-7.1");
    public static readonly OperationDescriptor PipelineList = Pipeline("pipeline list", "pipelines/list", "opaque header continuation");
    public static readonly OperationDescriptor PipelineGet = Pipeline("pipeline get", "pipelines/get", "none");
    public static readonly OperationDescriptor PipelineRuns = Pipeline("pipeline runs", "runs/list", "server cap 10000; no paging");
    public static readonly OperationDescriptor PipelineRunGet = Pipeline("pipeline run get", "runs/get", "none");
    public static readonly OperationDescriptor PipelineRunStart = new("pipeline run start", ServiceHost.Core, "7.1", true,
        true, "exact target", true, "none", "vso.build_execute",
        "https://learn.microsoft.com/en-us/rest/api/azure/devops/pipelines/runs/run-pipeline?view=azure-devops-rest-7.1");
    // Conservatively treated as a write for policy: preview still uses the queue endpoint via POST.
    public static readonly OperationDescriptor PipelineRunPreview = PipelineRunStart with { Command = "pipeline run preview" };
    public static readonly OperationDescriptor BuildList = new("build list", ServiceHost.Core, "7.1", false,
        true, "none", false, "opaque header continuation", "vso.build",
        "https://learn.microsoft.com/en-us/rest/api/azure/devops/build/builds/list?view=azure-devops-rest-7.1");
    public static readonly OperationDescriptor BuildGet = BuildList with
    {
        Command = "build get",
        Pagination = "none",
        Documentation = "https://learn.microsoft.com/en-us/rest/api/azure/devops/build/builds/get?view=azure-devops-rest-7.1"
    };
    public static IReadOnlyList<OperationDescriptor> All { get; } = [ProjectList, ProjectGet, PipelineList, PipelineGet, PipelineRuns, PipelineRunGet, PipelineRunStart, PipelineRunPreview, BuildList, BuildGet];

    private static OperationDescriptor Pipeline(string command, string endpoint, string pagination) => new(command,
        ServiceHost.Core, "7.1", false, true, "none", false, pagination, "vso.build",
        "https://learn.microsoft.com/en-us/rest/api/azure/devops/pipelines/" + endpoint + "?view=azure-devops-rest-7.1");
}

public static class SafetyPolicy
{
    public static void BeforeDispatch(OperationDescriptor operation, bool readOnly, bool dryRun)
    {
        if (operation.IsWrite && readOnly)
            throw new AdoException("read_only_refusal", "Read-only mode blocks this operation.", ExitCode.Safety);
        if (operation.IsWrite && dryRun)
            throw new AdoException("dry_run_dispatch_refusal", "Dry-run mutations must never reach HTTP dispatch.", ExitCode.Safety);
    }
}
