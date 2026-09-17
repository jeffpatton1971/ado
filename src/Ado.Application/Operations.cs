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
    public static IReadOnlyList<OperationDescriptor> All { get; } = [ProjectList, ProjectGet];
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
