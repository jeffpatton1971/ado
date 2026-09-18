using Ado.Application;
using Ado.Domain;
using Ado.Infrastructure;

namespace Ado.Cli;

internal static class BuildDiagnosisCommands
{
    public static async Task<int> ReadAsync(BuildsClient builds, BuildTimelineClient timelines, ServiceOptions options, int limit,
        TextWriter output, TextWriter error, CancellationToken cancellationToken)
    {
        var build = await builds.GetAsync(options.BuildId!.Value, cancellationToken);
        var timeline = await timelines.GetAsync(options.BuildId.Value, limit, cancellationToken, options.IncludeHistory);
        var diagnosis = BuildDiagnosis.Create(build.Items.Single(), timeline);
        bool incomplete = timeline.Meta.Completeness != "complete";
        if (options.Json)
        {
            if (options.RequireComplete && incomplete) await OutputWriter.PartialValueAsync(output, diagnosis, timeline.Meta);
            else await OutputWriter.SuccessAsync(output, diagnosis, true, timeline.Meta);
        }
        else
        {
            await output.WriteLineAsync($"BUILD {diagnosis.Build.Id}  STATUS {OutputWriter.TerminalSafe(diagnosis.Build.Status ?? "unknown")}  RESULT {OutputWriter.TerminalSafe(diagnosis.Build.Result ?? "unknown")}");
            await output.WriteLineAsync($"SOURCE {OutputWriter.TerminalSafe(diagnosis.Build.SourceBranch ?? "unknown")}  SHA {OutputWriter.TerminalSafe(diagnosis.Build.SourceVersion ?? "unknown")}");
            await output.WriteLineAsync("CATEGORY  ATTEMPT CONTEXT  ATTEMPT  NAME  LOG ID  TIMELINE ID");
            foreach (var finding in diagnosis.Findings)
                await output.WriteLineAsync($"{finding.Category}  {finding.AttemptContext}  {finding.Record.Attempt?.ToString() ?? "unknown"}  {OutputWriter.TerminalSafe(finding.Record.Name ?? "")}  {finding.Record.LogId?.ToString() ?? "unavailable"}  {finding.Record.TimelineId}");
            if (diagnosis.Findings.Count == 0) await output.WriteLineAsync("No failed, skipped, canceled or succeeded-with-issues records observed in loaded evidence.");
            foreach (string note in diagnosis.Limitations) await error.WriteLineAsync("note: " + note);
        }
        return options.RequireComplete && incomplete ? (int)ExitCode.Partial : 0;
    }
}
