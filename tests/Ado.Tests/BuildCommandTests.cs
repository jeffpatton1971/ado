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
    [DataRow("list", "--source-sha", "abc123")]
    [DataRow("list", "--repository-id", " ")]
    [DataRow("list", "--repository-type", "bad\nvalue")]
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
    public async Task ExactSourceSearchCanBeEmptyAndPartialThenResume()
    {
        string sha = new('a', 40);
        int calls = 0;
        using var handler = new TransportTests.FakeHandler(request =>
        {
            calls++;
            StringAssert.Contains(request.RequestUri!.Query, "repositoryId=repo%26id");
            StringAssert.Contains(request.RequestUri.Query, "repositoryType=TfsGit");
            Assert.IsFalse(request.RequestUri.Query.Contains("source", StringComparison.OrdinalIgnoreCase));
            if (calls == 2) StringAssert.Contains(request.RequestUri.Query, "continuationToken=next");
            var response = TransportTests.Json(JsonSerializer.Serialize(new { value = new[] { new { id = calls, definition = new { id = 12 }, sourceVersion = calls == 1 ? new string('b', 40) : sha.ToUpperInvariant() } } }));
            if (calls == 1) response.Headers.Add("x-ms-continuationtoken", "next");
            return response;
        });
        string[] args = ["list", "--source-sha", sha, "--repository-id", "repo&id", "--repository-type", "TfsGit", "--limit", "1", "--require-complete"];
        var first = await RunAsync(args, handler);
        Assert.AreEqual(10, first.Exit, first.Output);
        using var firstJson = JsonDocument.Parse(first.Output);
        Assert.AreEqual(0, firstJson.RootElement.GetProperty("data").GetArrayLength());
        var meta = firstJson.RootElement.GetProperty("meta");
        Assert.AreEqual(1, meta.GetProperty("scannedCount").GetInt32());
        Assert.AreEqual("scan_limit", meta.GetProperty("truncationReason").GetString());
        Assert.AreEqual("next", meta.GetProperty("continuationToken").GetString());
        var second = await RunAsync([.. args, "--continuation-token", "next"], handler);
        Assert.AreEqual(0, second.Exit, second.Output);
        using var secondJson = JsonDocument.Parse(second.Output);
        Assert.AreEqual(2, secondJson.RootElement.GetProperty("data")[0].GetProperty("id").GetInt32());
    }

    [TestMethod]
    public async Task MissingSourceVersionDoesNotClaimExhaustiveSearch()
    {
        using var handler = new TransportTests.FakeHandler(_ => TransportTests.Json("{\"value\":[" + Build + "]}"));
        var result = await RunAsync(["list", "--source-sha", new string('a', 40), "--require-complete"], handler);
        Assert.AreEqual(10, result.Exit, result.Output);
        using var json = JsonDocument.Parse(result.Output);
        Assert.AreEqual("unknown", json.RootElement.GetProperty("meta").GetProperty("completeness").GetString());
        Assert.AreEqual("source_version_unavailable", json.RootElement.GetProperty("meta").GetProperty("truncationReason").GetString());
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
