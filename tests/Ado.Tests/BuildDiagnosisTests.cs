using System.Text.Json;
using Ado.Application;
using Ado.Cli;
using Ado.Domain;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Ado.Tests;

[TestClass]
public sealed class BuildDiagnosisTests
{
    private static BuildTimelineRecord Record(Guid timeline, Guid id, string result, int attempt, string type = "Task") =>
        new(id, null, 34, "Same name", type, "completed", result, null, 1, 0, 0, attempt, TimelineId: timeline);

    [TestMethod]
    public void EarlierAttemptRequiresExactReferenceNotMatchingName()
    {
        var timeline = Guid.NewGuid(); var old = Guid.NewGuid(); var id = Guid.NewGuid();
        var current = Record(timeline, id, "succeeded", 2) with { PreviousAttempts = [new(1, id, old)] };
        var prior = Record(old, id, "failed", 1);
        var unrelated = Record(old, Guid.NewGuid(), "failed", 1);
        var container = Record(timeline, Guid.NewGuid(), "failed", 1, "Job");
        BuildInfo build = new(34, null, 12, null, "completed", "succeeded", null, null, null, null, null, null);
        var diagnosis = BuildDiagnosis.Create(build, new([current, prior, unrelated, container], new()));
        Assert.AreEqual(3, diagnosis.Findings.Count);
        Assert.AreEqual("referenced_previous_attempt", diagnosis.Findings[0].AttemptContext);
        Assert.AreEqual("not_identified_as_previous", diagnosis.Findings[1].AttemptContext);
        Assert.AreEqual("failed_container", diagnosis.Findings[2].Category);
        Assert.AreEqual("no_log_reference", diagnosis.Findings[0].LogAvailability);
    }

    [TestMethod]
    [DataRow(false, false)]
    [DataRow(true, false)]
    [DataRow(true, true)]
    public async Task CommandCombinesDetailsAndFindingsWithSafeOutput(bool json, bool partial)
    {
        using var handler = new TransportTests.FakeHandler(request =>
        {
            Assert.AreEqual(HttpMethod.Get, request.Method);
            Assert.AreEqual("dev.azure.com", request.RequestUri!.Host);
            if (request.RequestUri.AbsolutePath.EndsWith("/builds/34", StringComparison.Ordinal))
                return TransportTests.Json("""{"id":34,"definition":{"id":12},"status":"completed","result":"failed","sourceVersion":"abc123"}""");
            Assert.IsTrue(request.RequestUri.AbsolutePath.EndsWith("/timeline", StringComparison.Ordinal));
            return TransportTests.Json("""
                {"id":"aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa","records":[
                {"id":"bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb","type":"Task","name":"synthetic\u001b failure","result":"failed","attempt":1,"log":{"id":21}},
                {"id":"cccccccc-cccc-cccc-cccc-cccccccccccc","type":"Task","name":"Publish","result":"skipped","attempt":1},
                {"id":"dddddddd-dddd-dddd-dddd-dddddddddddd","type":"Job","name":"Deploy","result":"canceled","attempt":1}]}
                """);
        });
        using var output = new StringWriter(); using var error = new StringWriter();
        int exit = await CliApp.RunAsync(["build", "diagnose", "--run-url", "https://dev.azure.com/example/Project/_build/results?buildId=34",
            "--limit", partial ? "1" : "100", "--require-complete", "--read-only", "--non-interactive", "--output", json ? "json" : "table"],
            output, error, environment: key => key == "ADO_TOKEN" ? "synthetic" : null, testHandler: handler);
        Assert.AreEqual(partial ? 10 : 0, exit, output.ToString());
        Assert.AreEqual(2, handler.Calls);
        StringAssert.Contains(output.ToString(), "failed_task");
        StringAssert.Contains(output.ToString(), "21");
        Assert.IsFalse(output.ToString().Contains("synthetic", StringComparison.Ordinal));
        Assert.IsFalse(output.ToString().Contains('\u001b'));
        if (!partial) { StringAssert.Contains(output.ToString(), "skipped"); StringAssert.Contains(output.ToString(), "canceled_or_abandoned"); }
        if (json)
        {
            using var document = JsonDocument.Parse(output.ToString());
            Assert.AreEqual("abc123", document.RootElement.GetProperty("data").GetProperty("build").GetProperty("sourceVersion").GetString());
            Assert.AreEqual(partial ? 1 : 3, document.RootElement.GetProperty("data").GetProperty("findings").GetArrayLength());
        }
    }

    [TestMethod]
    public void MissingFailureEvidenceIsNotReportedAsSuccess()
    {
        BuildInfo build = new(34, null, 12, null, "completed", "failed", null, null, null, null, null, null);
        var result = BuildDiagnosis.Create(build, new([], new(Completeness: "unknown")));
        Assert.AreEqual(0, result.Findings.Count);
        Assert.IsTrue(result.Limitations.Any(note => note.Contains("no failed task", StringComparison.Ordinal)));
        Assert.IsTrue(result.Limitations.Any(note => note.Contains("incomplete", StringComparison.Ordinal)));
    }
}
