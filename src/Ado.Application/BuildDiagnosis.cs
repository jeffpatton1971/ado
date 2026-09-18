using Ado.Domain;

namespace Ado.Application;

public sealed record BuildFinding(BuildTimelineRecord Record, string Category, string AttemptContext, string? LogAvailability);
public sealed record BuildDiagnosis(BuildInfo Build, int LoadedRecords, IReadOnlyList<BuildFinding> Findings,
    IReadOnlyList<string> Limitations)
{
    public static BuildDiagnosis Create(BuildInfo build, CollectionResult<BuildTimelineRecord> timeline)
    {
        var previous = timeline.Items.SelectMany(record => record.PreviousAttempts ?? [])
            .Select(reference => (reference.TimelineId, reference.RecordId, reference.Attempt)).ToHashSet();
        var findings = new List<BuildFinding>();
        foreach (var record in timeline.Items)
        {
            string? category = record.Result switch
            {
                "failed" => record.Type == "Task" ? "failed_task" : "failed_container",
                "canceled" or "abandoned" => "canceled_or_abandoned",
                "skipped" => "skipped",
                "succeededWithIssues" => "succeeded_with_issues",
                _ => null
            };
            if (category is null) continue;
            bool historical = record.TimelineId is { } id && record.Attempt is { } attempt
                && previous.Contains((id, record.Id, attempt));
            findings.Add(new(record, category, historical ? "referenced_previous_attempt" : "not_identified_as_previous",
                record.LogId is null ? "no_log_reference" : "reference_only"));
        }
        var limitations = new List<string>
        {
            "Findings describe reported outcomes, not root causes. Skips/cancellations are not proven consequences of a specific failure.",
            "Only exact timeline/record/attempt references identify previous attempts; other records are not asserted to be the latest.",
            "Log IDs are references only; log contents and availability have not been checked. Use build log get with the build ID and selected log ID.",
            "Build details and timelines are separate reads and may change during an active run. SourceVersion identifies the build source, not all template revisions."
        };
        if (timeline.Meta.Completeness != "complete") limitations.Add("Timeline evidence is incomplete; omitted failures or retry outcomes cannot be ruled out.");
        if (build.Result == "failed" && !findings.Any(finding => finding.Category == "failed_task"))
            limitations.Add("The build failed but no failed task was observed in the loaded evidence.");
        return new(build, timeline.Items.Count, findings, limitations);
    }
}
