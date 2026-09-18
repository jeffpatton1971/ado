using System.Text.Json;
using Ado.Cli;
using Ado.Infrastructure.Http;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Ado.Tests;

[TestClass]
public sealed class BuildDownloadCommandTests
{
    [TestMethod]
    public void SignedArtifactRouteEncodesNamesAndBindsProject()
    {
        var uri = EndpointBuilder.PipelineArtifact("example", "My Project", 12, 34, "drop/a&b?#");
        Assert.AreEqual("https://dev.azure.com/example/My%20Project/_apis/pipelines/12/runs/34/artifacts?api-version=7.1&artifactName=drop%2Fa%26b%3F%23&%24expand=signedContent", uri.AbsoluteUri);
    }

    [TestMethod]
    public async Task DryRunDoesNotLookupCredentialsOrCreateFiles()
    {
        using var directory = new DownloadDirectory();
        using var handler = new TransportTests.FakeHandler(_ => throw new AssertFailedException());
        var result = await RunAsync(directory.Target, ["--dry-run"], handler, key => key == "ADO_TOKEN" ? throw new AssertFailedException("Token lookup") : null);
        Assert.AreEqual(0, result.Exit, result.Output);
        using var json = JsonDocument.Parse(result.Output);
        Assert.IsFalse(json.RootElement.GetProperty("data").GetProperty("downloaded").GetBoolean());
        Assert.AreEqual(0, Directory.GetFiles(directory.Root).Length);
        Assert.AreEqual(0, handler.Calls);
    }

    [TestMethod]
    [DataRow("Container")]
    [DataRow("PipelineArtifact")]
    public async Task ReadOnlyAllowsDownloadAfterMetadataAndWritesExactDestination(string resourceType)
    {
        using var directory = new DownloadDirectory();
        using var handler = new TransportTests.FakeHandler(request =>
        {
            if (request.RequestUri!.Host == "dev.azure.com")
            {
                Assert.IsNotNull(request.Headers.Authorization);
                if (request.RequestUri.AbsolutePath.EndsWith("/builds/34", StringComparison.Ordinal))
                    return TransportTests.Json("""{"id":34,"definition":{"id":12}}""");
                if (request.RequestUri.AbsolutePath.Contains("/pipelines/", StringComparison.Ordinal))
                {
                    Assert.AreEqual("https://dev.azure.com/example/Project/_apis/pipelines/12/runs/34/artifacts?api-version=7.1&artifactName=drop&%24expand=signedContent", request.RequestUri.AbsoluteUri);
                    return SignedContent();
                }
                if (request.Headers.Accept.Single().MediaType == "application/json")
                    return TransportTests.Json(JsonSerializer.Serialize(new { id = 7, name = "drop", resource = new { type = resourceType, downloadUrl = "https://evil.invalid/secret-sentinel" } }));
                Assert.AreEqual("Container", resourceType);
            }
            else
            {
                Assert.AreEqual("artprodcus3.artifacts.visualstudio.com", request.RequestUri.Host);
                Assert.IsNull(request.Headers.Authorization);
                Assert.IsFalse(request.Headers.Contains("Cookie"));
                Assert.IsFalse(request.Headers.Contains("X-TFS-FedAuthRedirect"));
                Assert.AreEqual("?sig=secret-sentinel", request.RequestUri.Query);
            }
            return BuildDownloadTests.Content(BuildDownloadTests.Zip());
        });
        var result = await RunAsync(directory.Target, [], handler);
        Assert.AreEqual(0, result.Exit, result.Output);
        Assert.IsTrue(File.Exists(directory.Target));
        Assert.AreEqual(resourceType == "Container" ? 2 : 4, handler.Calls);
        Assert.IsFalse(result.Output.Contains("secret-sentinel", StringComparison.Ordinal));
    }

    private static System.Net.Http.HttpResponseMessage SignedContent(string name = "drop",
        string url = "https://artprodcus3.artifacts.visualstudio.com/content?sig=secret-sentinel",
        string expires = "2099-01-01T00:00:00Z") => TransportTests.Json(JsonSerializer.Serialize(new
        { name, signedContent = new { url, signatureExpires = expires } }));

    [TestMethod]
    [DataRow("wrongName")]
    [DataRow("expired")]
    [DataRow("missing")]
    [DataRow("unsafeHost")]
    [DataRow("identityRedirect")]
    [DataRow("wrongBuild")]
    public async Task SignedContentFailuresNeverPublishOrForwardCredentials(string mode)
    {
        using var directory = new DownloadDirectory();
        int contentCalls = 0;
        using var handler = new TransportTests.FakeHandler(request =>
        {
            if (request.RequestUri!.Host != "dev.azure.com")
            {
                Assert.AreEqual("identityRedirect", mode);
                Assert.AreEqual("artprodcus3.artifacts.visualstudio.com", request.RequestUri.Host);
                Assert.IsNull(request.Headers.Authorization);
                contentCalls++;
                return new(System.Net.HttpStatusCode.Redirect)
                { Headers = { Location = new("https://spsprodcus2.vssps.visualstudio.com/signin?secret-sentinel") } };
            }
            if (request.RequestUri.AbsolutePath.EndsWith("/builds/34", StringComparison.Ordinal))
                return TransportTests.Json(mode == "wrongBuild" ? """{"id":35,"definition":{"id":12}}""" : """{"id":34,"definition":{"id":12}}""");
            if (!request.RequestUri.AbsolutePath.Contains("/pipelines/", StringComparison.Ordinal))
                return TransportTests.Json("""{"id":7,"name":"drop","resource":{"type":"PipelineArtifact"}}""");
            return mode switch
            {
                "wrongName" => SignedContent(name: "other"),
                "expired" => SignedContent(expires: "2000-01-01T00:00:00Z"),
                "missing" => TransportTests.Json("""{"name":"drop"}"""),
                "unsafeHost" => SignedContent(url: "https://evil.invalid/secret-sentinel"),
                _ => SignedContent()
            };
        });
        var result = await RunAsync(directory.Target, [], handler);
        Assert.AreNotEqual(0, result.Exit);
        Assert.AreEqual(mode == "identityRedirect" ? 1 : 0, contentCalls);
        Assert.AreEqual(0, Directory.GetFiles(directory.Root).Length);
        Assert.IsFalse(result.Output.Contains("secret-sentinel", StringComparison.Ordinal));
        Assert.AreEqual(mode == "wrongBuild" ? 2 : mode == "identityRedirect" ? 4 : 3, handler.Calls);
    }

    [TestMethod]
    public async Task UnsupportedResourceStopsBeforeContentRequest()
    {
        using var directory = new DownloadDirectory();
        using var handler = new TransportTests.FakeHandler(_ => TransportTests.Json("""{"id":7,"name":"drop","resource":{"type":"FilePath"}}"""));
        var result = await RunAsync(directory.Target, [], handler);
        Assert.AreEqual(7, result.Exit, result.Output);
        Assert.AreEqual(1, handler.Calls);
        Assert.AreEqual(0, Directory.GetFiles(directory.Root).Length);
    }

    [TestMethod]
    public async Task ExistingDestinationFailsBeforeCredentialProvider()
    {
        using var directory = new DownloadDirectory();
        await File.WriteAllTextAsync(directory.Target, "existing");
        using var handler = new TransportTests.FakeHandler(_ => throw new AssertFailedException());
        var result = await RunAsync(directory.Target, [], handler, _ => null);
        Assert.AreEqual(7, result.Exit, result.Output);
        Assert.AreEqual("existing", await File.ReadAllTextAsync(directory.Target));
        Assert.AreEqual(0, handler.Calls);
    }

    [TestMethod]
    [DataRow("--max-bytes", "0")]
    [DataRow("--max-bytes", "1073741825")]
    [DataRow("--download-timeout", "601")]
    public async Task InvalidOverridesFailLocally(string option, string value)
    {
        using var directory = new DownloadDirectory();
        using var handler = new TransportTests.FakeHandler(_ => throw new AssertFailedException());
        var result = await RunAsync(directory.Target, [option, value], handler, _ => null);
        Assert.AreEqual(2, result.Exit, result.Output);
        Assert.AreEqual(0, handler.Calls);
    }

    private static async Task<(int Exit, string Output)> RunAsync(string destination, string[] extra, HttpMessageHandler handler, Func<string, string?>? environment = null)
    {
        string path = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(path, """{"defaultProfile":"test","profiles":{"test":{"organization":"example","project":"Project"}}}""");
            using var output = new StringWriter();
            using var error = new StringWriter();
            int exit = await CliApp.RunAsync(["build", "artifact", "download", "--build-id", "34", "--artifact-name", "drop", "--destination", destination,
                .. extra, "--config", path, "--json", "--read-only", "--non-interactive"], output, error,
                environment: environment ?? (key => key == "ADO_TOKEN" ? "synthetic" : null), testHandler: handler);
            return (exit, output.ToString());
        }
        finally { File.Delete(path); }
    }
}
