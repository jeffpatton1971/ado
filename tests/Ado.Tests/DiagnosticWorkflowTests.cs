using System.Net;
using System.Text.Json;
using Ado.Cli;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Ado.Tests;

[TestClass]
public sealed class DiagnosticWorkflowTests
{
    private const string Url = "https://dev.azure.com/example/Project/_build/results?buildId=34";
    private const string Prior = "bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb";
    private const string Build = """{"id":34,"definition":{"id":12},"status":"completed","result":"failed","sourceVersion":"abc123"}""";
    private const string Timeline = """
        {"id":"aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa","records":[
        {"id":"cccccccc-cccc-cccc-cccc-cccccccccccc","type":"Task","name":"Compile","result":"failed","attempt":2,"log":{"id":21},
         "previousAttempts":[{"attempt":1,"recordId":"cccccccc-cccc-cccc-cccc-cccccccccccc","timelineId":"bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"}]},
        {"id":"dddddddd-dddd-dddd-dddd-dddddddddddd","type":"Task","name":"Publish","result":"skipped","attempt":1}]}
        """;
    private const string PriorTimeline = """
        {"id":"bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb","records":[
        {"id":"cccccccc-cccc-cccc-cccc-cccccccccccc","type":"Task","name":"Compile","result":"failed","attempt":1,"log":{"id":20}}]}
        """;

    [TestMethod]
    [DataRow("complete", 0)]
    [DataRow("range", 10)]
    [DataRow("limited", 10)]
    [DataRow("missingLog", 6)]
    [DataRow("forbiddenLog", 5)]
    public async Task DiagnoseThenSelectTargetedLogFromJson(string mode, int expectedLogExit)
    {
        var paths = new List<string>();
        using var handler = new TransportTests.FakeHandler(request =>
        {
            Assert.AreEqual(HttpMethod.Get, request.Method);
            Assert.AreEqual("dev.azure.com", request.RequestUri!.Host);
            Assert.IsNotNull(request.Headers.Authorization);
            string path = request.RequestUri.AbsolutePath;
            paths.Add(path);
            if (path.EndsWith("/builds/34", StringComparison.Ordinal)) return TransportTests.Json(Build);
            if (path.EndsWith("/timeline", StringComparison.Ordinal)) return TransportTests.Json(Timeline);
            if (path.EndsWith("/timeline/" + Prior, StringComparison.Ordinal)) return TransportTests.Json(PriorTimeline);
            Assert.AreEqual("/example/Project/_apis/build/builds/34/logs/21", path);
            if (mode == "range") StringAssert.Contains(request.RequestUri.Query, "startLine=10&endLine=12");
            if (mode is "missingLog" or "forbiddenLog") return new(mode == "missingLog" ? HttpStatusCode.NotFound : HttpStatusCode.Forbidden)
            { Content = new StringContent("secret-server-error") };
            return TransportTests.Json(JsonSerializer.Serialize("synthetic failure\nsecond line\nthird line"));
        });
        var diagnosis = await Run(["diagnose", "--include-history", "--require-complete"], handler);
        Assert.AreEqual(0, diagnosis.Exit, diagnosis.Output);
        using var document = JsonDocument.Parse(diagnosis.Output);
        var findings = document.RootElement.GetProperty("data").GetProperty("findings").EnumerateArray().ToArray();
        Assert.AreEqual(1, findings.Count(f => f.GetProperty("attemptContext").GetString() == "referenced_previous_attempt"));
        var selected = findings.Single(f => f.GetProperty("category").GetString() == "failed_task"
            && f.GetProperty("attemptContext").GetString() == "not_identified_as_previous");
        int logId = selected.GetProperty("record").GetProperty("logId").GetInt32();
        Assert.AreEqual(3, paths.Count); // Diagnosis has not fetched any logs.
        string[] range = mode == "range" ? ["--start-line", "10", "--end-line", "12"] : [];
        var log = await Run(["log", "get", "--log-id", logId.ToString(), "--require-complete", "--limit", mode == "limited" ? "1" : "100", .. range], handler);
        Assert.AreEqual(expectedLogExit, log.Exit, log.Output);
        Assert.AreEqual(4, paths.Count);
        Assert.IsFalse(log.Output.Contains("synthetic", StringComparison.Ordinal));
        Assert.IsFalse(log.Output.Contains("secret-server-error", StringComparison.Ordinal));
        using var logJson = JsonDocument.Parse(log.Output);
        if (expectedLogExit is 0 or 10)
        {
            var data = logJson.RootElement.GetProperty("data");
            Assert.AreEqual(21, data.GetProperty("logId").GetInt32());
            Assert.AreEqual(mode == "limited" ? 1 : 3, data.GetProperty("lines").GetArrayLength());
            Assert.AreEqual(expectedLogExit == 0, logJson.RootElement.GetProperty("ok").GetBoolean());
        }
        else Assert.IsFalse(logJson.RootElement.TryGetProperty("data", out _));
    }

    [TestMethod]
    [DataRow(401, 4)]
    [DataRow(403, 5)]
    public async Task TimelineAccessFailureDoesNotBecomeSuccessfulDiagnosis(int status, int expectedExit)
    {
        using var handler = new TransportTests.FakeHandler(request => request.RequestUri!.AbsolutePath.EndsWith("/builds/34", StringComparison.Ordinal)
            ? TransportTests.Json(Build) : new HttpResponseMessage((HttpStatusCode)status) { Content = new StringContent("secret-server-error") });
        var result = await Run(["diagnose", "--include-history"], handler);
        Assert.AreEqual(expectedExit, result.Exit, result.Output);
        Assert.AreEqual(2, handler.Calls);
        using var document = JsonDocument.Parse(result.Output);
        Assert.IsFalse(document.RootElement.GetProperty("ok").GetBoolean());
        Assert.IsFalse(document.RootElement.TryGetProperty("data", out _));
        Assert.IsFalse(result.Output.Contains("secret-server-error", StringComparison.Ordinal));
    }

    private static async Task<(int Exit, string Output)> Run(string[] commands, HttpMessageHandler handler)
    {
        using var output = new StringWriter(); using var error = new StringWriter();
        using var input = new StringReader("synthetic\n");
        int exit = await CliApp.RunAsync(["build", .. commands, "--run-url", Url, "--token-stdin", "--json", "--non-interactive", "--read-only"],
            output, error, input: input, environment: _ => null, testHandler: handler);
        Assert.AreEqual("", error.ToString(), "JSON automation must not prompt or mix diagnostics into stdout.");
        using var parsed = JsonDocument.Parse(output.ToString()); // Exactly one envelope per invocation.
        return (exit, output.ToString());
    }
}
