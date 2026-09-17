using System.Text.Json;
using Ado.Cli;
using Ado.Application;
using Ado.Domain;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Ado.Tests;

[TestClass]
public sealed class ServiceCommandTests
{
    private const string Project = """{"id":"00000000-0000-0000-0000-000000000001","name":"Demo","state":"wellFormed"}""";

    [TestMethod]
    public async Task ProjectGetUsesBearerStdinWithoutIncidentalStdout()
    {
        using var handler = new TransportTests.FakeHandler(request =>
        {
            Assert.AreEqual("Bearer", request.Headers.Authorization!.Scheme);
            Assert.AreEqual("opaque bearer", request.Headers.Authorization.Parameter);
            StringAssert.Contains(request.RequestUri!.AbsoluteUri, "Demo%20Project");
            return TransportTests.Json(Project);
        });
        var result = await RunAsync(["project", "get", "--project", "Demo Project", "--token-stdin", "--auth-type", "entra-token"], handler, new StringReader("opaque bearer\n"));
        Assert.AreEqual(0, result.Exit);
        using var json = JsonDocument.Parse(result.Output);
        Assert.AreEqual("Demo", json.RootElement.GetProperty("data").GetProperty("name").GetString());
        Assert.IsFalse(result.Output.Contains("opaque bearer", StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task StrictCompletenessPreservesPartialDataAndReturnsFailureEnvelope()
    {
        using var handler = new TransportTests.FakeHandler(_ =>
        {
            var response = TransportTests.Json("{\"value\":[" + Project + "]}");
            response.Headers.Add("x-ms-continuationtoken", "1");
            return response;
        });
        var result = await RunAsync(["project", "list", "--limit", "1", "--require-complete"], handler);
        Assert.AreEqual(10, result.Exit);
        using var json = JsonDocument.Parse(result.Output);
        Assert.IsFalse(json.RootElement.GetProperty("ok").GetBoolean());
        Assert.IsTrue(json.RootElement.GetProperty("meta").GetProperty("truncated").GetBoolean());
        Assert.AreEqual(1, json.RootElement.GetProperty("data").GetArrayLength());
    }

    [TestMethod]
    public async Task DoctorDoesNotRetrieveTokenOrUseNetwork()
    {
        using var handler = new TransportTests.FakeHandler(_ => throw new AssertFailedException("No network"));
        var result = await RunAsync(["doctor"], handler, environment: key => key == "ADO_TOKEN" ? throw new AssertFailedException("No token lookup") : null);
        Assert.AreEqual(0, result.Exit);
        Assert.AreEqual(0, handler.Calls);
        using var json = JsonDocument.Parse(result.Output);
        Assert.AreEqual("not_tested", json.RootElement.GetProperty("data").GetProperty("credentialAccess").GetString());
    }

    [TestMethod]
    public async Task DirectTokenWarnsWithoutDisclosure()
    {
        using var handler = new TransportTests.FakeHandler(_ => TransportTests.Json("{\"value\":[]}"));
        var result = await RunAsync(["auth", "check", "--token", "secret-sentinel"], handler);
        Assert.AreEqual(0, result.Exit);
        StringAssert.Contains(result.Error, "process listings");
        Assert.IsFalse((result.Output + result.Error).Contains("secret-sentinel", StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task ConflictingSourcesAndJsonPromptFailBeforeNetwork()
    {
        using var handler = new TransportTests.FakeHandler(_ => throw new AssertFailedException("No network"));
        Assert.AreEqual(2, (await RunAsync(["project", "list", "--token", "fake", "--token-stdin"], handler)).Exit);
        Assert.AreEqual(4, (await RunAsync(["project", "list", "--token-prompt"], handler)).Exit);
        Assert.AreEqual(0, handler.Calls);
    }

    [TestMethod]
    public void CredentialSourceSelectionIsAtomicAndExplicitInputWins()
    {
        var configured = new CredentialReference { Provider = "macos-keychain", Service = "work", Account = "pat" };
        var selected = CredentialSelection.Resolve(configured, null, false, true, false, null, null, null, key => key == "ADO_TOKEN" ? "ignored" : null);
        Assert.AreEqual("stdin", selected.Reference.Provider);
        Assert.IsNull(selected.Reference.Service);
        Assert.IsTrue(selected.OverridesProfile);
        Assert.ThrowsExactly<AdoException>(() => CredentialSelection.Resolve(configured, null, false, false, false, "macos-keychain", null, null, _ => null));
    }

    [TestMethod]
    public async Task HumanOutputEscapesTerminalControlCharacters()
    {
        using var handler = new TransportTests.FakeHandler(_ => TransportTests.Json(Project.Replace("Demo", "Demo\\u001b[31m", StringComparison.Ordinal)));
        var result = await RunAsync(["project", "get", "--project", "Demo", "--output", "table"], handler, json: false);
        Assert.AreEqual(0, result.Exit);
        Assert.IsFalse(result.Output.Contains('\u001b'));
        StringAssert.Contains(result.Output, "\\u001b");
    }

    private static async Task<(int Exit, string Output, string Error)> RunAsync(string[] args, HttpMessageHandler handler,
        TextReader? input = null, Func<string, string?>? environment = null, bool json = true)
    {
        string path = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(path, "{}");
            using var output = new StringWriter();
            using var error = new StringWriter();
            string[] fullArgs = [.. args, "--config", path, "--organization", "example", .. (json ? new[] { "--json" } : Array.Empty<string>())];
            int exit = await CliApp.RunAsync(fullArgs, output, error, environment: environment ?? (key => key == "ADO_TOKEN" ? "fake-test-token" : null), input: input, testHandler: handler);
            return (exit, output.ToString(), error.ToString());
        }
        finally { File.Delete(path); }
    }
}
