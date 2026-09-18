using System.Text.Json;
using Ado.Application;
using Ado.Cli;
using Ado.Domain;
using Ado.Infrastructure;
using Ado.Infrastructure.Http;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Ado.Tests;

[TestClass]
public sealed class BuildTimelineTests
{
    private const string Record = """{"id":"11111111-1111-1111-1111-111111111111","name":"synthetic task\u001b","type":"Task","state":"completed","result":"failed","errorCount":1,"log":{"id":3,"url":"secret-sentinel"},"issues":[{"message":"secret-sentinel"}],"workerName":"secret-sentinel"}""";

    [TestMethod]
    [DataRow("complete")]
    [DataRow("limited")]
    [DataRow("nested")]
    public async Task ReadsBoundedSafeTimeline(string mode)
    {
        string record = mode == "nested" ? Record.Replace("\"errorCount\":1", "\"details\":{\"id\":\"22222222-2222-2222-2222-222222222222\"},\"errorCount\":1", StringComparison.Ordinal) : Record;
        using var handler = new TransportTests.FakeHandler(request =>
        {
            Assert.AreEqual("https://dev.azure.com/example/My%20Project/_apis/build/builds/34/timeline?api-version=7.1", request.RequestUri!.AbsoluteUri);
            return TransportTests.Json("{\"records\":[" + record + (mode == "limited" ? "," + Record : "") + "]}");
        });
        using var http = new HttpClient(handler);
        using var auth = new TokenAuthentication("pat", new("synthetic"));
        var result = await new BuildTimelineClient(new ServiceTransport(http, auth, "example"), "example", "My Project").GetAsync(34, 1, CancellationToken.None);
        Assert.AreEqual(3, result.Items[0].LogId);
        Assert.AreEqual("[REDACTED] task\u001b", result.Items[0].Name);
        Assert.AreEqual(mode == "complete" ? "complete" : mode == "limited" ? "partial" : "unknown", result.Meta.Completeness);
        Assert.IsFalse(JsonSerializer.Serialize(result).Contains("secret-sentinel", StringComparison.Ordinal));
    }

    [TestMethod]
    [DataRow("{}")]
    [DataRow("{\"records\":[{\"id\":\"bad\"}]}")]
    [DataRow("{\"records\":[{\"id\":\"11111111-1111-1111-1111-111111111111\",\"log\":{\"id\":-1}}]}")]
    public async Task RejectsMalformedTimeline(string body)
    {
        using var handler = new TransportTests.FakeHandler(_ => TransportTests.Json(body));
        using var http = new HttpClient(handler);
        using var auth = new TokenAuthentication("pat", new("synthetic"));
        await Assert.ThrowsExactlyAsync<AdoException>(() => new BuildTimelineClient(new ServiceTransport(http, auth, "example"), "example", "Project").GetAsync(34, 100, CancellationToken.None));
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task CliPreservesPartialRecordsAndEscapesTerminal(bool json)
    {
        using var handler = new TransportTests.FakeHandler(_ => TransportTests.Json("{\"records\":[" + Record + "," + Record + "]}"));
        using var output = new StringWriter();
        using var error = new StringWriter();
        int exit = await CliApp.RunAsync(["build", "timeline", "--build-id", "34", "--organization", "example", "--project", "Project",
            "--limit", "1", "--require-complete", "--read-only", "--non-interactive", "--output", json ? "json" : "table"], output, error,
            environment: key => key == "ADO_TOKEN" ? "synthetic" : null, testHandler: handler);
        Assert.AreEqual(10, exit, output.ToString());
        Assert.IsFalse(output.ToString().Contains('\u001b'));
        Assert.IsFalse(output.ToString().Contains("secret-sentinel", StringComparison.Ordinal));
        if (json)
        {
            using var document = JsonDocument.Parse(output.ToString());
            Assert.AreEqual(1, document.RootElement.GetProperty("data").GetArrayLength());
        }
        else Assert.IsTrue(output.ToString().Contains("LOG ID", StringComparison.Ordinal));
    }
}
