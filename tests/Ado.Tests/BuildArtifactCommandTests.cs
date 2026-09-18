using System.Text.Json;
using Ado.Cli;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Ado.Tests;

[TestClass]
public sealed class BuildArtifactCommandTests
{
    private const string Item = """{"id":7,"name":"drop","resource":{"type":"Container","downloadUrl":"https://evil.invalid/secret-sentinel"}}""";

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task CommandsUseReadOnlyJsonMetadataAndNeverFollowLinks(bool single)
    {
        using var handler = new TransportTests.FakeHandler(request =>
        {
            Assert.AreEqual(HttpMethod.Get, request.Method);
            Assert.AreEqual("/example/Backend/_apis/build/builds/34/artifacts", request.RequestUri!.AbsolutePath);
            if (single) StringAssert.Contains(request.RequestUri.Query, "artifactName=drop");
            return TransportTests.Json(single ? Item : "{\"value\":[" + Item + "]}");
        });
        var result = await RunAsync(single ? ["get", "--artifact-name", "drop"] : ["list", "--dry-run"], handler);
        Assert.AreEqual(0, result.Exit, result.Output);
        using var json = JsonDocument.Parse(result.Output);
        Assert.AreEqual(single ? JsonValueKind.Object : JsonValueKind.Array, json.RootElement.GetProperty("data").ValueKind);
        Assert.IsFalse(result.Output.Contains("secret-sentinel", StringComparison.Ordinal));
        Assert.AreEqual(1, handler.Calls);
    }

    [TestMethod]
    [DataRow("get", "--artifact-name", " ")]
    [DataRow("get", "--artifact-name", "bad\nname")]
    [DataRow("list", "--top", "1")]
    [DataRow("list", "--continuation-token", "1")]
    public async Task InvalidInputsFailBeforeCredentials(string command, string option, string value)
    {
        using var handler = new TransportTests.FakeHandler(_ => throw new AssertFailedException());
        Assert.AreEqual(2, (await RunAsync([command, option, value], handler, token: false)).Exit);
        Assert.AreEqual(0, handler.Calls);
    }

    [TestMethod]
    public async Task MissingNameFailsBeforeCredentials()
    {
        using var handler = new TransportTests.FakeHandler(_ => throw new AssertFailedException());
        Assert.AreEqual(2, (await RunAsync(["get"], handler, token: false)).Exit);
        Assert.AreEqual(0, handler.Calls);
    }

    [TestMethod]
    public async Task StrictLimitReturnsPartialArrayWithoutContinuation()
    {
        using var handler = new TransportTests.FakeHandler(_ => TransportTests.Json("{\"value\":[" + Item + "," + Item.Replace("\"id\":7", "\"id\":8", StringComparison.Ordinal) + "]}"));
        var result = await RunAsync(["list", "--limit", "1", "--require-complete"], handler);
        Assert.AreEqual(10, result.Exit);
        using var json = JsonDocument.Parse(result.Output);
        Assert.IsFalse(json.RootElement.GetProperty("ok").GetBoolean());
        Assert.AreEqual(1, json.RootElement.GetProperty("data").GetArrayLength());
        Assert.AreEqual(JsonValueKind.Null, json.RootElement.GetProperty("meta").GetProperty("continuationToken").ValueKind);
    }

    [TestMethod]
    public async Task HumanTableEscapesUntrustedOutputNames()
    {
        using var handler = new TransportTests.FakeHandler(_ => TransportTests.Json("""{"value":[{"id":7,"name":"drop\u001b[31m","resource":{"type":"Container"}}]}"""));
        var result = await RunAsync(["list"], handler, json: false);
        Assert.AreEqual(0, result.Exit);
        StringAssert.Contains(result.Output, "OUTPUT ID  BUILD ID");
        StringAssert.Contains(result.Output, "drop\\u001b[31m");
        Assert.IsFalse(result.Output.Contains('\u001b'));
    }

    private static async Task<(int Exit, string Output)> RunAsync(string[] args, HttpMessageHandler handler, bool token = true, bool json = true)
    {
        string path = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(path, """{"defaultProfile":"test","profiles":{"test":{"organization":"example","project":"Backend"}}}""");
            using var output = new StringWriter();
            using var error = new StringWriter();
            int exit = await CliApp.RunAsync(["build", "artifact", .. args, "--build-id", "34", "--config", path, "--output", json ? "json" : "table", "--non-interactive", "--read-only"], output, error,
                environment: key => key == "ADO_TOKEN" && token ? "synthetic" : null, testHandler: handler);
            return (exit, output.ToString());
        }
        finally { File.Delete(path); }
    }
}
