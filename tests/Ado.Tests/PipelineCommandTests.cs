using System.Text.Json;
using Ado.Cli;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Ado.Tests;

[TestClass]
public sealed class PipelineCommandTests
{
    [TestMethod]
    [DataRow("list", "/pipelines", """{"value":[{"id":12,"name":"Backend"}]}""")]
    [DataRow("get", "/pipelines/12", """{"id":12,"name":"Backend","configuration":{"type":"yaml"}}""")]
    [DataRow("runs", "/pipelines/12/runs", """{"value":[{"id":34,"pipeline":{"id":12},"state":"completed"}]}""")]
    [DataRow("run get", "/pipelines/12/runs/34", """{"id":34,"pipeline":{"id":12},"state":"completed","result":"succeeded"}""")]
    public async Task EachCommandUsesConfiguredProjectAndReadOnlyHttp(string command, string suffix, string response)
    {
        using var handler = new TransportTests.FakeHandler(request =>
        {
            Assert.AreEqual(HttpMethod.Get, request.Method);
            Assert.AreEqual("/example/Backend%20Project/_apis" + suffix, request.RequestUri!.AbsolutePath);
            return TransportTests.Json(response);
        });
        string[] arguments = ["pipeline", .. command.Split(' '), .. (command == "list" ? System.Array.Empty<string>() : new[] { "--pipeline-id", "12" }),
            .. (command == "run get" ? new[] { "--run-id", "34" } : System.Array.Empty<string>())];
        var result = await RunAsync(arguments, handler);
        Assert.AreEqual(0, result.Exit);
        using var json = JsonDocument.Parse(result.Output);
        Assert.IsTrue(json.RootElement.GetProperty("ok").GetBoolean());
        Assert.AreEqual("Backend Project", json.RootElement.GetProperty("meta").GetProperty("project").GetString());
    }

    [TestMethod]
    [DataRow("--top")]
    [DataRow("--continuation-token")]
    public async Task RunsRejectUndocumentedPagingFlags(string flag)
    {
        using var handler = new TransportTests.FakeHandler(_ => throw new AssertFailedException());
        var result = await RunAsync(["pipeline", "runs", "--pipeline-id", "12", flag, "1"], handler);
        Assert.AreEqual(2, result.Exit);
        Assert.AreEqual(0, handler.Calls);
    }

    [TestMethod]
    public async Task RequiredIdsFailBeforeNetwork()
    {
        using var handler = new TransportTests.FakeHandler(_ => throw new AssertFailedException());
        Assert.AreEqual(2, (await RunAsync(["pipeline", "get"], handler)).Exit);
        Assert.AreEqual(2, (await RunAsync(["pipeline", "run", "get", "--pipeline-id", "12"], handler)).Exit);
        Assert.AreEqual(0, handler.Calls);
    }

    [TestMethod]
    public async Task StrictRunCompletenessReturnsPartialDataAndExitTen()
    {
        using var handler = new TransportTests.FakeHandler(_ => TransportTests.Json("""{"value":[{"id":34,"pipeline":{"id":12}},{"id":35,"pipeline":{"id":12}}]}"""));
        var result = await RunAsync(["pipeline", "runs", "--pipeline-id", "12", "--limit", "1", "--require-complete"], handler);
        Assert.AreEqual(10, result.Exit);
        using var json = JsonDocument.Parse(result.Output);
        Assert.IsFalse(json.RootElement.GetProperty("ok").GetBoolean());
        Assert.AreEqual("item_limit", json.RootElement.GetProperty("meta").GetProperty("truncationReason").GetString());
        Assert.AreEqual(JsonValueKind.Null, json.RootElement.GetProperty("meta").GetProperty("continuationToken").ValueKind);
    }

    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public async Task RunGetPresentsRepositoryVersionsAndEscapesServiceControls(bool jsonOutput)
    {
        using var handler = new TransportTests.FakeHandler(_ => TransportTests.Json("""
            {"id":34,"pipeline":{"id":12},"resources":{"repositories":{
              "shared\u001b[31m":{"repository":{"type":"azureReposGit"},"refName":"refs/heads/main","version":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"}
            }}}
            """));
        var result = await RunAsync(["pipeline", "run", "get", "--pipeline-id", "12", "--run-id", "34"], handler, jsonOutput);
        Assert.AreEqual(0, result.Exit, result.Output);
        if (jsonOutput)
        {
            using var json = JsonDocument.Parse(result.Output);
            var provenance = json.RootElement.GetProperty("data").GetProperty("repositoryProvenance");
            Assert.AreEqual("reported_versions", provenance.GetProperty("status").GetString());
            Assert.AreEqual(new string('a', 40), provenance.GetProperty("repositories")[0].GetProperty("version").GetString());
        }
        else
        {
            StringAssert.Contains(result.Output, "REPOSITORY PROVENANCE reported_versions");
            StringAssert.Contains(result.Output, "shared\\u001b[31m");
            Assert.IsFalse(result.Output.Contains('\u001b'));
            StringAssert.Contains(result.Output, new string('a', 40));
        }
        Assert.AreEqual(1, handler.Calls);
    }

    private static async Task<(int Exit, string Output)> RunAsync(string[] args, HttpMessageHandler handler, bool json = true)
    {
        string path = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(path, """{"defaultProfile":"test","profiles":{"test":{"organization":"example","project":"Backend Project"}}}""");
            using var output = new StringWriter();
            using var error = new StringWriter();
            int exit = await CliApp.RunAsync([.. args, "--config", path, "--output", json ? "json" : "table", "--read-only", "--non-interactive"], output, error,
                environment: key => key == "ADO_TOKEN" ? "synthetic" : null, testHandler: handler);
            return (exit, output.ToString());
        }
        finally { File.Delete(path); }
    }
}
