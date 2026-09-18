using System.Net;
using System.Text.Json;
using Ado.Application;
using Ado.Cli;
using Ado.Domain;
using Ado.Infrastructure;
using Ado.Infrastructure.Http;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Ado.Tests;

[TestClass]
public sealed class ReleaseTaskLogTests
{
    private const string Metadata = """{"id":1492,"releaseDefinition":{"id":20},"environments":[{"id":1499,"deploySteps":[{"id":3615,"deploymentId":1506,"attempt":1,"releaseDeployPhases":[{"phaseId":77,"deploymentJobs":[{"tasks":[{"id":12,"logUrl":"https://evil.invalid/secret-sentinel"}]}]}]}]}]}""";
    private static HttpResponseMessage Log(string body) => new(HttpStatusCode.OK) { Content = new StringContent(body) };

    [TestMethod]
    [DataRow(false, false)]
    [DataRow(true, false)]
    [DataRow(false, true)]
    [DataRow(true, true)]
    public async Task ResolvesPhaseAndReadsRedactedBoundedText(bool range, bool stringPhase)
    {
        using var handler = new TransportTests.FakeHandler(request =>
        {
            Assert.AreEqual("vsrm.dev.azure.com", request.RequestUri!.Host);
            Assert.IsNotNull(request.Headers.Authorization);
            if (request.Headers.Accept.Single().MediaType == "application/json") return TransportTests.Json(stringPhase ? Metadata.Replace("\"phaseId\":77", "\"phaseId\":\"77\"", StringComparison.Ordinal) : Metadata);
            Assert.AreEqual("https://vsrm.dev.azure.com/example/Project/_apis/release/releases/1492/environments/1499/deployPhases/77/tasks/12/logs?api-version=7.1" + (range ? "&startLine=2&endLine=4" : ""), request.RequestUri.AbsoluteUri);
            Assert.AreEqual("text/plain", request.Headers.Accept.Single().MediaType);
            return Log("synthetic\u001b first\r\nsecond\nthird\n");
        });
        using var http = new HttpClient(handler);
        using var auth = new TokenAuthentication("pat", new("synthetic"));
        var result = await new ReleasesClient(new ServiceTransport(http, auth, "example"), "example", "Project")
            .TaskLogAsync(1492, 1499, 1506, 12, range ? 2 : null, range ? 4 : null, range ? 10 : 2, CancellationToken.None);
        Assert.AreEqual(77, result.Data.PhaseId);
        Assert.AreEqual("[REDACTED]\u001b first", result.Data.Lines[0]);
        Assert.AreEqual(range ? "unknown" : "partial", result.Meta.Completeness);
        Assert.AreEqual(2, handler.Calls);
    }

    [TestMethod]
    [DataRow("\"0\"")]
    [DataRow("\"2147483648\"")]
    [DataRow("\"77/secret-sentinel\"")]
    [DataRow("\"+77\"")]
    [DataRow("{}")]
    public async Task InvalidPhaseNeverFetchesLog(string phase)
    {
        using var handler = new TransportTests.FakeHandler(_ => TransportTests.Json(
            Metadata.Replace("\"phaseId\":77", "\"phaseId\":" + phase, StringComparison.Ordinal)));
        using var http = new HttpClient(handler);
        using var auth = new TokenAuthentication("pat", new("synthetic"));
        var error = await Assert.ThrowsExactlyAsync<AdoException>(() => new ReleasesClient(new ServiceTransport(http, auth, "example"), "example", "Project")
            .TaskLogAsync(1492, 1499, 1506, 12, null, null, 100, CancellationToken.None));
        Assert.AreEqual("invalid_service_response", error.Code);
        StringAssert.Contains(error.Message, "phaseId");
        Assert.IsFalse(error.Message.Contains("secret-sentinel", StringComparison.Ordinal));
        Assert.AreEqual(1, handler.Calls);
    }

    [TestMethod]
    [DataRow("missingPhase")]
    [DataRow("wrongDeployment")]
    [DataRow("missingTasks")]
    public async Task UnresolvedContextNeverFetchesLog(string mode)
    {
        string body = mode switch
        {
            "missingPhase" => Metadata.Replace("\"phaseId\":77,", "", StringComparison.Ordinal),
            "wrongDeployment" => Metadata.Replace("1506", "1507", StringComparison.Ordinal),
            _ => Metadata.Replace("\"tasks\"", "\"other\"", StringComparison.Ordinal)
        };
        using var handler = new TransportTests.FakeHandler(_ => TransportTests.Json(body));
        using var http = new HttpClient(handler);
        using var auth = new TokenAuthentication("pat", new("synthetic"));
        var error = await Assert.ThrowsExactlyAsync<AdoException>(() => new ReleasesClient(new ServiceTransport(http, auth, "example"), "example", "Project")
            .TaskLogAsync(1492, 1499, 1506, 12, null, null, 100, CancellationToken.None));
        Assert.AreEqual("unresolved_task_log", error.Code);
        Assert.AreEqual(1, handler.Calls);
    }

    [TestMethod]
    [DataRow("html")]
    [DataRow("redirect")]
    [DataRow("oversized")]
    [DataRow("invalidUtf8")]
    [DataRow("continuation")]
    public async Task RejectsUnsafeOrInvalidLogResponses(string mode)
    {
        using var handler = new TransportTests.FakeHandler(request =>
        {
            if (request.Headers.Accept.Single().MediaType == "application/json") return TransportTests.Json(Metadata);
            var response = Log("secret-sentinel");
            if (mode == "html") response.Content.Headers.ContentType = new("text/html");
            if (mode == "redirect") { response.StatusCode = HttpStatusCode.Redirect; response.Headers.Location = new("https://evil.invalid/secret-sentinel"); }
            if (mode == "oversized") response.Content.Headers.ContentLength = 4194305;
            if (mode == "continuation") response.Headers.Add("x-ms-continuationtoken", "42");
            if (mode == "invalidUtf8") response.Content = new ByteArrayContent([0xff]) { Headers = { ContentType = new("text/plain") } };
            return response;
        });
        using var http = new HttpClient(handler);
        using var auth = new TokenAuthentication("pat", new("synthetic"));
        var error = await Assert.ThrowsExactlyAsync<AdoException>(() => new ReleasesClient(new ServiceTransport(http, auth, "example"), "example", "Project")
            .TaskLogAsync(1492, 1499, 1506, 12, null, null, 100, CancellationToken.None));
        Assert.IsFalse(error.Message.Contains("secret-sentinel", StringComparison.Ordinal));
        Assert.AreEqual(2, handler.Calls);
    }

    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public async Task CliRetainsPartialLogsAndEscapesControls(bool json)
    {
        using var handler = new TransportTests.FakeHandler(request => request.Headers.Accept.Single().MediaType == "application/json"
            ? TransportTests.Json(Metadata) : Log("synthetic\u001b line\nsecond"));
        using var output = new StringWriter();
        using var error = new StringWriter();
        int exit = await CliApp.RunAsync(["release", "task", "log", "--release-id", "1492", "--environment-id", "1499", "--deployment-id", "1506", "--task-id", "12",
            "--organization", "example", "--project", "Project", "--limit", "1", "--require-complete", "--read-only", "--non-interactive", "--output", json ? "json" : "table"], output, error,
            environment: key => key == "ADO_TOKEN" ? "synthetic" : null, testHandler: handler);
        Assert.AreEqual(10, exit, output.ToString());
        Assert.IsFalse(output.ToString().Contains("synthetic", StringComparison.Ordinal));
        Assert.IsFalse(output.ToString().Contains('\u001b'));
        if (json)
        {
            using var document = JsonDocument.Parse(output.ToString());
            Assert.AreEqual(1, document.RootElement.GetProperty("data").GetProperty("lines").GetArrayLength());
        }
    }
}
