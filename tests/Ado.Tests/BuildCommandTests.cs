using System.Text.Json;
using Ado.Cli;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Ado.Tests;

[TestClass]
public sealed class BuildCommandTests
{
    private const string Build = """{"id":34,"definition":{"id":12,"name":"Backend"},"buildNumber":"20260917.1","status":"completed","result":"failed","sourceBranch":"refs/heads/main"}""";

    [TestMethod]
    public async Task ListSendsServerFiltersAndGetReturnsObject()
    {
        using var listHandler = new TransportTests.FakeHandler(request =>
        {
            Assert.AreEqual(HttpMethod.Get, request.Method);
            Assert.AreEqual("/example/Backend%20Project/_apis/build/builds", request.RequestUri!.AbsolutePath);
            foreach (string query in new[] { "definitions=12", "statusFilter=completed", "resultFilter=failed", "branchName=refs%2Fheads%2Fmain", "%24top=1" })
                StringAssert.Contains(request.RequestUri.Query, query);
            return TransportTests.Json("{\"value\":[" + Build + "]}");
        });
        var list = await RunAsync(["list", "--definition-id", "12", "--status", "completed", "--result", "failed", "--branch", "refs/heads/main", "--limit", "1"], listHandler);
        Assert.AreEqual(0, list.Exit, list.Output);
        using var listJson = JsonDocument.Parse(list.Output);
        Assert.AreEqual(JsonValueKind.Array, listJson.RootElement.GetProperty("data").ValueKind);
        using var getHandler = new TransportTests.FakeHandler(request =>
        {
            Assert.AreEqual("/example/Backend%20Project/_apis/build/builds/34", request.RequestUri!.AbsolutePath);
            Assert.AreEqual(HttpMethod.Get, request.Method);
            return TransportTests.Json(Build);
        });
        var get = await RunAsync(["get", "--build-id", "34"], getHandler);
        Assert.AreEqual(0, get.Exit, get.Output);
        using var getJson = JsonDocument.Parse(get.Output);
        Assert.AreEqual(34, getJson.RootElement.GetProperty("data").GetProperty("id").GetInt32());
        Assert.AreEqual("Backend Project", getJson.RootElement.GetProperty("meta").GetProperty("project").GetString());
    }

    [TestMethod]
    [DataRow("get", "--build-id", "0")]
    [DataRow("get", "--pipeline-id", "12")]
    [DataRow("list", "--definition-id", "0")]
    [DataRow("list", "--status", "canceling")]
    [DataRow("list", "--result", "success")]
    [DataRow("list", "--branch", " ")]
    [DataRow("list", "--top", "0")]
    [DataRow("list", "--continuation-token", "bad\nvalue")]
    public async Task InvalidInputsFailBeforeCredentialAcquisition(string command, string flag, string value)
    {
        using var handler = new TransportTests.FakeHandler(_ => throw new AssertFailedException());
        var result = await RunAsync([command, flag, value], handler, token: false);
        Assert.AreEqual(2, result.Exit, result.Output);
        Assert.AreEqual(0, handler.Calls);
    }

    [TestMethod]
    public async Task MissingBuildIdFailsBeforeCredentialAcquisition()
    {
        using var handler = new TransportTests.FakeHandler(_ => throw new AssertFailedException());
        Assert.AreEqual(2, (await RunAsync(["get"], handler, token: false)).Exit);
        Assert.AreEqual(0, handler.Calls);
    }

    [TestMethod]
    public async Task StrictCompletenessKeepsPartialDataAndContinuation()
    {
        using var handler = new TransportTests.FakeHandler(_ =>
        {
            var response = TransportTests.Json("{\"value\":[" + Build + "]}");
            response.Headers.Add("x-ms-continuationtoken", "opaque+/token");
            return response;
        });
        var result = await RunAsync(["list", "--limit", "1", "--require-complete"], handler);
        Assert.AreEqual(10, result.Exit);
        using var json = JsonDocument.Parse(result.Output);
        Assert.IsFalse(json.RootElement.GetProperty("ok").GetBoolean());
        Assert.AreEqual(1, json.RootElement.GetProperty("data").GetArrayLength());
        Assert.AreEqual("opaque+/token", json.RootElement.GetProperty("meta").GetProperty("continuationToken").GetString());
    }

    [TestMethod]
    public async Task TableEscapesServiceControlsAndReportsTruncationOnStderr()
    {
        using var handler = new TransportTests.FakeHandler(_ =>
        {
            var response = TransportTests.Json("""{"value":[{"id":34,"definition":{"id":12},"buildNumber":"hello\u001b[31m","sourceBranch":"a\nb"}]}""");
            response.Headers.Add("x-ms-continuationtoken", "next");
            return response;
        });
        var result = await RunAsync(["list", "--limit", "1"], handler, json: false);
        Assert.AreEqual(0, result.Exit);
        StringAssert.Contains(result.Output, "BUILD ID  DEFINITION ID");
        StringAssert.Contains(result.Output, "hello\\u001b[31m");
        StringAssert.Contains(result.Output, "a\\u000ab");
        Assert.IsFalse(result.Output.Contains('\u001b'));
        StringAssert.Contains(result.Error, "Results are truncated");
    }

    [TestMethod]
    public async Task EmptyListIsCompleteAndDryRunStillReads()
    {
        using var handler = new TransportTests.FakeHandler(_ => TransportTests.Json("{\"value\":[]}"));
        var result = await RunAsync(["list", "--dry-run", "--all", "--require-complete"], handler);
        Assert.AreEqual(0, result.Exit);
        using var json = JsonDocument.Parse(result.Output);
        Assert.AreEqual(0, json.RootElement.GetProperty("data").GetArrayLength());
        Assert.AreEqual("complete", json.RootElement.GetProperty("meta").GetProperty("completeness").GetString());
        Assert.AreEqual(1, handler.Calls);
    }

    private static async Task<(int Exit, string Output, string Error)> RunAsync(string[] args, HttpMessageHandler handler, bool json = true, bool token = true)
    {
        string path = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(path, """{"defaultProfile":"test","profiles":{"test":{"organization":"example","project":"Backend Project"}}}""");
            using var output = new StringWriter();
            using var error = new StringWriter();
            int exit = await CliApp.RunAsync(["build", .. args, "--config", path, "--output", json ? "json" : "table", "--non-interactive", "--read-only"], output, error,
                environment: key => key == "ADO_TOKEN" && token ? "synthetic" : null, testHandler: handler);
            return (exit, output.ToString(), error.ToString());
        }
        finally { File.Delete(path); }
    }
}
