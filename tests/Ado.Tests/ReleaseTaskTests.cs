using System.Text.Json;
using Ado.Application;
using Ado.Cli;
using Ado.Domain;
using Ado.Infrastructure;
using Ado.Infrastructure.Http;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Ado.Tests;

[TestClass]
public sealed class ReleaseTaskTests
{
    private const string TaskJson = """{"id":4,"name":"synthetic\u001b task","status":"failed","lineCount":2147483648,"issues":[{"message":"secret-sentinel"}],"logUrl":"https://secret-sentinel","agentName":"secret-sentinel","task":{"inputs":{"value":"secret-sentinel"}}}""";
    private static string Body(string tasks) => """{"id":1492,"releaseDefinition":{"id":20},"environments":[{"id":1499,"releaseId":1492,"deploySteps":[{"id":3615,"deploymentId":1506,"attempt":1,"releaseDeployPhases":[{"name":"Phase","deploymentJobs":[{"job":{"id":1,"name":"Job"},"tasks":TASKS}]}]}]}]}""".Replace("TASKS", tasks, StringComparison.Ordinal);

    [TestMethod]
    [DataRow("empty", 0, "complete")]
    [DataRow("single", 1, "complete")]
    [DataRow("limited", 1, "partial")]
    [DataRow("missing", 0, "unknown")]
    public async Task ExpandsAndBoundsSafeTaskResults(string mode, int count, string completeness)
    {
        string tasks = mode == "empty" ? "[]" : mode == "missing" ? "null" : "[" + TaskJson +
            (mode == "limited" ? "," + TaskJson.Replace("\"id\":4", "\"id\":5", StringComparison.Ordinal) : "") + "]";
        using var handler = new TransportTests.FakeHandler(request =>
        {
            Assert.AreEqual("https://vsrm.dev.azure.com/example/Project/_apis/release/releases/1492?api-version=7.1&%24expand=tasks", request.RequestUri!.AbsoluteUri);
            Assert.IsNotNull(request.Headers.Authorization);
            return TransportTests.Json(Body(tasks));
        });
        using var http = new HttpClient(handler);
        using var auth = new TokenAuthentication("pat", new("synthetic"));
        var result = await new ReleasesClient(new ServiceTransport(http, auth, "example"), "example", "Project").TasksAsync(1492, 1, CancellationToken.None);
        Assert.AreEqual(count, result.Items.Count);
        Assert.AreEqual(completeness, result.Meta.Completeness);
        if (count > 0)
        {
            Assert.AreEqual("[REDACTED]\u001b task", result.Items[0].Name);
            Assert.AreEqual(1506, result.Items[0].DeploymentId);
            Assert.AreEqual(2147483648L, result.Items[0].LineCount);
            Assert.AreEqual("Job", result.Items[0].JobName);
        }
        Assert.IsFalse(JsonSerializer.Serialize(result).Contains("secret-sentinel", StringComparison.Ordinal));
    }

    [TestMethod]
    [DataRow("duplicate")]
    [DataRow("wrongRelease")]
    [DataRow("wrongEnvironmentRelease")]
    [DataRow("badJob")]
    [DataRow("badArray")]
    [DataRow("badLines")]
    [DataRow("continuation")]
    public async Task RejectsMalformedOrMismatchedResults(string mode)
    {
        string body = Body(mode == "badArray" ? "{}" : "[" + TaskJson + (mode == "duplicate" ? "," + TaskJson : "") + "]");
        body = mode switch
        {
            "wrongRelease" => body.Replace("\"id\":1492", "\"id\":1491", StringComparison.Ordinal),
            "wrongEnvironmentRelease" => body.Replace("\"releaseId\":1492", "\"releaseId\":1491", StringComparison.Ordinal),
            "badJob" => body.Replace("\"id\":1,", "\"id\":0,", StringComparison.Ordinal),
            "badLines" => body.Replace("2147483648", "-1", StringComparison.Ordinal),
            _ => body
        };
        using var handler = new TransportTests.FakeHandler(_ =>
        {
            var response = TransportTests.Json(body);
            if (mode == "continuation") response.Headers.Add("x-ms-continuationtoken", "42");
            return response;
        });
        using var http = new HttpClient(handler);
        using var auth = new TokenAuthentication("pat", new("synthetic"));
        await Assert.ThrowsExactlyAsync<AdoException>(() => new ReleasesClient(new ServiceTransport(http, auth, "example"), "example", "Project")
            .TasksAsync(1492, 100, CancellationToken.None));
    }

    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public async Task CliRetainsBoundedResults(bool json)
    {
        using var handler = new TransportTests.FakeHandler(_ => TransportTests.Json(Body("[" + TaskJson + "," + TaskJson.Replace("\"id\":4", "\"id\":5", StringComparison.Ordinal) + "]")));
        using var output = new StringWriter();
        using var error = new StringWriter();
        int exit = await CliApp.RunAsync(["release", "tasks", "--release-id", "1492", "--organization", "example", "--project", "Project",
            "--limit", "1", "--require-complete", "--read-only", "--non-interactive", "--output", json ? "json" : "table"], output, error,
            environment: key => key == "ADO_TOKEN" ? "synthetic" : null, testHandler: handler);
        Assert.AreEqual(10, exit, output.ToString());
        Assert.IsFalse(output.ToString().Contains("secret-sentinel", StringComparison.Ordinal));
        Assert.IsFalse(output.ToString().Contains('\u001b'));
        if (json)
        {
            using var document = JsonDocument.Parse(output.ToString());
            Assert.AreEqual(1, document.RootElement.GetProperty("data").GetArrayLength());
        }
        else Assert.IsTrue(output.ToString().Contains("TASK ID", StringComparison.Ordinal));
    }
}
