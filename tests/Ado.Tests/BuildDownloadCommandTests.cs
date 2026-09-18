using System.Text.Json;
using Ado.Cli;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Ado.Tests;

[TestClass]
public sealed class BuildDownloadCommandTests
{
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
    public async Task ReadOnlyAllowsDownloadAfterMetadataAndWritesExactDestination()
    {
        using var directory = new DownloadDirectory();
        using var handler = new TransportTests.FakeHandler(request => request.Headers.Accept.Single().MediaType == "application/json"
            ? TransportTests.Json("""{"id":7,"name":"drop","resource":{"type":"PipelineArtifact","downloadUrl":"https://evil.invalid/secret-sentinel"}}""")
            : BuildDownloadTests.Content(BuildDownloadTests.Zip()));
        var result = await RunAsync(directory.Target, [], handler);
        Assert.AreEqual(0, result.Exit, result.Output);
        Assert.IsTrue(File.Exists(directory.Target));
        Assert.AreEqual(2, handler.Calls);
        Assert.IsFalse(result.Output.Contains("secret-sentinel", StringComparison.Ordinal));
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
