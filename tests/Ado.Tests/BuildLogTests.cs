using System.Text.Json;
using Ado.Application;
using Ado.Cli;
using Ado.Domain;
using Ado.Infrastructure;
using Ado.Infrastructure.Http;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Ado.Tests;

[TestClass]
public sealed class BuildLogTests
{
    [TestMethod]
    public async Task ExplicitRangeDoesNotClaimWholeLogCompleteness()
    {
        using var handler = new TransportTests.FakeHandler(request =>
        {
            StringAssert.Contains(request.RequestUri!.Query, "startLine=10&endLine=20");
            return TransportTests.Json("\"one line\"");
        });
        var result = await RunAsync(["log", "get", "--log-id", "2", "--start-line", "10", "--end-line", "20", "--require-complete"], handler);
        Assert.AreEqual(10, result.Exit, result.Output);
        using var json = JsonDocument.Parse(result.Output);
        Assert.IsFalse(json.RootElement.GetProperty("ok").GetBoolean());
        Assert.AreEqual(34, json.RootElement.GetProperty("data").GetProperty("buildId").GetInt32());
        Assert.AreEqual("unknown", json.RootElement.GetProperty("meta").GetProperty("completeness").GetString());
    }

    [TestMethod]
    public async Task TextEscapesControlsAndRedactsCredential()
    {
        using var handler = new TransportTests.FakeHandler(_ => TransportTests.Json("\"hello synthetic\\u001b[31m\\nnext\\tline\""));
        var result = await RunAsync(["log", "get", "--log-id", "2"], handler, json: false);
        Assert.AreEqual(0, result.Exit, result.Output);
        StringAssert.Contains(result.Output, "hello [REDACTED]\\u001b[31m");
        StringAssert.Contains(result.Output, "next\\u0009line");
        Assert.IsFalse(result.Output.Contains('\u001b'));
        Assert.IsFalse(result.Output.Contains("synthetic", StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task IndexCliSupportsStrictCompleteness()
    {
        using var handler = new TransportTests.FakeHandler(_ => TransportTests.Json("{\"value\":[{\"id\":2},{\"id\":3}]}"));
        var result = await RunAsync(["logs", "--limit", "1", "--require-complete"], handler);
        Assert.AreEqual(10, result.Exit, result.Output);
        using var json = JsonDocument.Parse(result.Output);
        Assert.AreEqual(1, json.RootElement.GetProperty("data").GetArrayLength());
    }

    [TestMethod]
    [DataRow("--log-id", "0")]
    [DataRow("--start-line", "-1")]
    [DataRow("--end-line", "-1")]
    [DataRow("--top", "1")]
    [DataRow("--continuation-token", "1")]
    public async Task InvalidOptionsDoNotRetrieveCredentialsOrSend(string option, string value)
    {
        using var handler = new TransportTests.FakeHandler(_ => throw new AssertFailedException());
        var result = await RunAsync(["log", "get", .. (option == "--log-id" ? Array.Empty<string>() : new[] { "--log-id", "2" }), option, value], handler, token: false);
        Assert.AreEqual(2, result.Exit, result.Output);
        Assert.AreEqual(0, handler.Calls);
    }

    [TestMethod]
    public async Task MissingLogAndReversedRangeFailBeforeCredentials()
    {
        using var handler = new TransportTests.FakeHandler(_ => throw new AssertFailedException());
        Assert.AreEqual(2, (await RunAsync(["log", "get"], handler, token: false)).Exit);
        Assert.AreEqual(2, (await RunAsync(["log", "get", "--log-id", "2", "--start-line", "20", "--end-line", "10"], handler, token: false)).Exit);
        Assert.AreEqual(0, handler.Calls);
    }

    [TestMethod]
    [DataRow("[\"one\",123]")]
    [DataRow("{\"unexpected\":\"secret-sentinel\"}")]
    public async Task InvalidLogPayloadFailsWithoutEcho(string content)
    {
        using var handler = new TransportTests.FakeHandler(_ => TransportTests.Json(content));
        var result = await RunAsync(["log", "get", "--log-id", "2"], handler);
        Assert.AreEqual(9, result.Exit);
        Assert.IsFalse(result.Output.Contains("secret-sentinel", StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task OversizedLogResponseFailsWithNoPartialSecretOutput()
    {
        using var handler = new TransportTests.FakeHandler(_ =>
        {
            var response = TransportTests.Json("\"secret-sentinel\"");
            response.Content.Headers.ContentLength = 5 * 1024 * 1024;
            return response;
        });
        var result = await RunAsync(["log", "get", "--log-id", "2"], handler);
        Assert.AreEqual(10, result.Exit);
        Assert.IsFalse(result.Output.Contains("secret-sentinel", StringComparison.Ordinal));
    }

    private static async Task<(int Exit, string Output, string Error)> RunAsync(string[] args, HttpMessageHandler handler, bool json = true, bool token = true)
    {
        string path = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(path, """{"defaultProfile":"test","profiles":{"test":{"organization":"example","project":"Backend"}}}""");
            using var output = new StringWriter();
            using var error = new StringWriter();
            int exit = await CliApp.RunAsync(["build", .. args, "--build-id", "34", "--config", path, "--output", json ? "json" : "table", "--non-interactive", "--read-only"], output, error,
                environment: key => key == "ADO_TOKEN" && token ? "synthetic" : null, testHandler: handler);
            return (exit, output.ToString(), error.ToString());
        }
        finally { File.Delete(path); }
    }
}
