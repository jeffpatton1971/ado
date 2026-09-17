using System.Text.Json;
using Ado.Cli;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Ado.Tests;

[TestClass]
public sealed class PipelinePreviewCommandTests
{
    private const string Confirmation = "pipeline run preview:example/Sandbox/12";

    [TestMethod]
    public async Task LocalPreviewOfServerPreviewDoesNotRetrieveTokenOrSendHttp()
    {
        using var handler = new TransportTests.FakeHandler(_ => throw new AssertFailedException());
        var result = await RunAsync(["--dry-run", "--read-only"], handler,
            key => key == "ADO_TOKEN" ? throw new AssertFailedException("Must not read token") : null);
        Assert.AreEqual(0, result.Exit, result.Output);
        using var json = JsonDocument.Parse(result.Output);
        var data = json.RootElement.GetProperty("data");
        Assert.AreEqual("pipeline run preview", data.GetProperty("action").GetString());
        Assert.AreEqual(Confirmation, data.GetProperty("requiredConfirmation").GetString());
        Assert.IsTrue(data.GetProperty("previewRun").GetBoolean());
        Assert.IsFalse(data.GetProperty("submitted").GetBoolean());
        Assert.AreEqual(0, handler.Calls);
    }

    [TestMethod]
    [DataRow(null, false)]
    [DataRow("pipeline run start:example/Sandbox/12", false)]
    [DataRow(Confirmation, true)]
    public async Task GuardsRejectBeforeCredentialProvider(string? confirmation, bool readOnly)
    {
        using var handler = new TransportTests.FakeHandler(_ => throw new AssertFailedException());
        var result = await RunAsync([.. (confirmation is null ? Array.Empty<string>() : new[] { "--confirm", confirmation }),
            .. (readOnly ? new[] { "--read-only" } : Array.Empty<string>())], handler, _ => null);
        Assert.AreEqual(7, result.Exit, result.Output);
        Assert.AreEqual(0, handler.Calls);
    }

    [TestMethod]
    public async Task DefaultServerPreviewOmitsYamlAndAllOtherResponseFields()
    {
        using var handler = new PreviewHandler();
        var result = await RunAsync(["--confirm", Confirmation], handler);
        Assert.AreEqual(0, result.Exit, result.Output);
        using var json = JsonDocument.Parse(result.Output);
        var data = json.RootElement.GetProperty("data");
        Assert.IsTrue(data.GetProperty("previewRun").GetBoolean());
        Assert.AreEqual(JsonValueKind.Null, data.GetProperty("finalYaml").ValueKind);
        Assert.IsTrue(data.GetProperty("yamlCharacters").GetInt32() > 0);
        Assert.IsFalse(result.Output.Contains("secret-sentinel", StringComparison.Ordinal));
        Assert.IsFalse(result.Output.Contains("synthetic-token", StringComparison.Ordinal));
        Assert.AreEqual(1, handler.Calls);
    }

    [TestMethod]
    public async Task ExplicitYamlUsesEscapedJsonAndWarnsOnlyOnStderr()
    {
        using var handler = new PreviewHandler();
        var result = await RunAsync(["--confirm", Confirmation, "--show-yaml"], handler);
        Assert.AreEqual(0, result.Exit, result.Output);
        using var json = JsonDocument.Parse(result.Output);
        string yaml = json.RootElement.GetProperty("data").GetProperty("finalYaml").GetString()!;
        StringAssert.Contains(yaml, "steps: []");
        StringAssert.Contains(yaml, "[REDACTED]");
        Assert.IsFalse(result.Output.Contains("synthetic-token", StringComparison.Ordinal));
        Assert.IsFalse(result.Output.Contains('\u001b'));
        StringAssert.Contains(result.Error, "Treat --show-yaml output as sensitive");
    }

    [TestMethod]
    public async Task ParameterNamedPreviewRunCannotOverrideTheTransportFlag()
    {
        string path = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(path, """{"previewRun":false}""");
            using var handler = new PreviewHandler();
            var result = await RunAsync(["--confirm", Confirmation, "--parameters-file", path], handler);
            Assert.AreEqual(0, result.Exit, result.Output);
            using var body = JsonDocument.Parse(handler.Body!);
            Assert.IsTrue(body.RootElement.GetProperty("previewRun").GetBoolean());
            Assert.IsFalse(body.RootElement.GetProperty("templateParameters").GetProperty("previewRun").GetBoolean());
        }
        finally { File.Delete(path); }
    }

    [TestMethod]
    public async Task InvalidJsonResponseIsPreviewFailureNotUncertainWrite()
    {
        using var handler = new TransportTests.FakeHandler(_ => TransportTests.Json("secret-sentinel"));
        var result = await RunAsync(["--confirm", Confirmation], handler);
        Assert.AreEqual(9, result.Exit, result.Output);
        using var json = JsonDocument.Parse(result.Output);
        Assert.AreEqual("preview_failed", json.RootElement.GetProperty("error").GetProperty("code").GetString());
        Assert.IsFalse(result.Output.Contains("secret-sentinel", StringComparison.Ordinal));
        Assert.AreEqual(1, handler.Calls);
    }

    private sealed class PreviewHandler : HttpMessageHandler
    {
        public int Calls { get; private set; }
        public string? Body { get; private set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            Assert.AreEqual(HttpMethod.Post, request.Method);
            Body = await request.Content!.ReadAsStringAsync(cancellationToken);
            using var body = JsonDocument.Parse(Body);
            Assert.IsTrue(body.RootElement.GetProperty("previewRun").GetBoolean());
            return TransportTests.Json("""{"finalYaml":"steps: [] # synthetic-token\n# \u001b[31m secret-sentinel","variables":{"password":{"value":"secret-sentinel"}},"url":"https://example.invalid/secret-sentinel"}""");
        }
    }

    private static async Task<(int Exit, string Output, string Error)> RunAsync(string[] args, HttpMessageHandler handler,
        Func<string, string?>? environment = null)
    {
        string path = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(path, """{"defaultProfile":"test","profiles":{"test":{"organization":"example","project":"Sandbox"}}}""");
            using var output = new StringWriter();
            using var error = new StringWriter();
            int exit = await CliApp.RunAsync(["pipeline", "run", "preview", "--pipeline-id", "12", .. args, "--config", path, "--json", "--non-interactive"], output, error,
                environment: environment ?? (key => key == "ADO_TOKEN" ? "synthetic-token" : null), testHandler: handler);
            return (exit, output.ToString(), error.ToString());
        }
        finally { File.Delete(path); }
    }
}
