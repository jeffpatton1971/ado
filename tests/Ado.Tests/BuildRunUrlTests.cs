using Ado.Cli;
using Ado.Domain;
using Ado.Infrastructure.Http;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Ado.Tests;

[TestClass]
public sealed class BuildRunUrlTests
{
    [TestMethod]
    [DataRow("https://dev.azure.com/example/Backend%20Project/_build/results?buildId=34&view=logs#ignored")]
    [DataRow("https://example.visualstudio.com/Backend%20Project/_build/results?buildId=34")]
    public async Task UrlConstructsCanonicalReadEndpoint(string url)
    {
        using var handler = new TransportTests.FakeHandler(request =>
        {
            Assert.AreEqual("https://dev.azure.com/example/Backend%20Project/_apis/build/builds/34?api-version=7.1", request.RequestUri!.AbsoluteUri);
            Assert.AreEqual(HttpMethod.Get, request.Method);
            return TransportTests.Json("""{"id":34,"definition":{"id":12},"status":"completed"}""");
        });
        using var output = new StringWriter();
        using var error = new StringWriter();
        int exit = await CliApp.RunAsync(["build", "get", "--run-url", url, "--json", "--non-interactive", "--read-only"], output, error,
            environment: key => key == "ADO_TOKEN" ? "synthetic" : null, testHandler: handler);
        Assert.AreEqual(0, exit, output.ToString());
        Assert.AreEqual(1, handler.Calls);
    }

    [TestMethod]
    [DataRow("http://dev.azure.com/example/P/_build/results?buildId=34")]
    [DataRow("https://dev.azure.com.evil.invalid/example/P/_build/results?buildId=34")]
    [DataRow("https://a.b.visualstudio.com/P/_build/results?buildId=34")]
    [DataRow("https://user@dev.azure.com/example/P/_build/results?buildId=34")]
    [DataRow("https://dev.azure.com:444/example/P/_build/results?buildId=34")]
    [DataRow("https://dev.azure.com/example/P%2Fother/_build/results?buildId=34")]
    [DataRow("https://dev.azure.com/example/P/_build/results?buildId=0")]
    [DataRow("https://dev.azure.com/example/P/_build/results?buildId=2147483648")]
    [DataRow("https://dev.azure.com/example/P/_build/results?buildId=34&buildId=34")]
    [DataRow("https://dev.azure.com/example/P/_build/results?view=logs")]
    [DataRow("https://github.com/example/repo/checks/34")]
    public void RejectsUnsupportedUrlsWithoutEchoingInput(string url)
    {
        var error = Assert.ThrowsExactly<AdoException>(() => BuildRunUrl.Parse(url));
        Assert.AreEqual("invalid_run_url", error.Code);
        Assert.IsFalse(error.Message.Contains(url, StringComparison.Ordinal));
    }

    [TestMethod]
    [DataRow("--organization", "other")]
    [DataRow("--project", "other")]
    [DataRow("--build-id", "35")]
    [DataRow("environment", "other")]
    public async Task ContextMismatchFailsBeforePromptOrHttp(string option, string value)
    {
        using var handler = new TransportTests.FakeHandler(_ => throw new AssertFailedException("Unexpected HTTP"));
        using var output = new StringWriter();
        using var error = new StringWriter();
        string[] extra = option == "environment" ? [] : [option, value];
        int exit = await CliApp.RunAsync(["build", "get", "--run-url", "https://dev.azure.com/example/P/_build/results?buildId=34", "--token-prompt", .. extra],
            output, error, input: new StringReader(""), environment: key => option == "environment" && key == "ADO_ORGANIZATION" ? value : null, testHandler: handler);
        Assert.AreEqual(2, exit);
        StringAssert.Contains(error.ToString(), "run_url_context_mismatch");
        Assert.IsFalse(error.ToString().Contains("Token:", StringComparison.Ordinal));
        Assert.AreEqual(0, handler.Calls);
    }
}
