using System.Text.Json;
using Ado.Cli;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Ado.Tests;

[TestClass]
public sealed class PipelineStartCommandTests
{
    private const string Confirmation = "pipeline run start:example/Backend Project/12";

    [TestMethod]
    public async Task DryRunIsLocalAndOmitsAllValues()
    {
        string parameters = Path.GetTempFileName(), variables = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(parameters, """{"environment":"secret-parameter","enabled":true,"count":3}""");
            await File.WriteAllTextAsync(variables, """{"password":{"value":"secret-variable","isSecret":true},"plain":{"value":"also-private"}}""");
            using var handler = new TransportTests.FakeHandler(_ => throw new AssertFailedException());
            var result = await RunAsync(["--dry-run", "--read-only", "--parameters-file", parameters, "--variables-file", variables], handler,
                key => key == "ADO_TOKEN" ? throw new AssertFailedException("Dry run must not look up a token") : null);
            Assert.AreEqual(0, result.Exit, result.Output);
            Assert.AreEqual(0, handler.Calls);
            using var json = JsonDocument.Parse(result.Output);
            var data = json.RootElement.GetProperty("data");
            Assert.AreEqual(Confirmation, data.GetProperty("requiredConfirmation").GetString());
            Assert.IsFalse(data.GetProperty("submitted").GetBoolean());
            Assert.AreEqual(3, data.GetProperty("parameterCount").GetInt32());
            Assert.AreEqual(2, data.GetProperty("variableCount").GetInt32());
            foreach (string secret in new[] { "secret-parameter", "secret-variable", "also-private", "password" })
                Assert.IsFalse(result.Output.Contains(secret, StringComparison.Ordinal));
        }
        finally { File.Delete(parameters); File.Delete(variables); }
    }

    [TestMethod]
    [DataRow(false, null)]
    [DataRow(false, "yes")]
    [DataRow(false, "pipeline run start:example/Backend Project/13")]
    [DataRow(true, Confirmation)]
    public async Task ConfirmationAndReadOnlyFailBeforeCredentialAcquisition(bool readOnly, string? confirmation)
    {
        using var handler = new TransportTests.FakeHandler(_ => throw new AssertFailedException());
        var result = await RunAsync([.. (readOnly ? new[] { "--read-only" } : Array.Empty<string>()),
            .. (confirmation is null ? Array.Empty<string>() : new[] { "--confirm", confirmation })], handler, _ => null);
        Assert.AreEqual(7, result.Exit, result.Output); // A credential lookup would instead fail with exit 4.
        Assert.AreEqual(0, handler.Calls);
    }

    [TestMethod]
    public async Task ConfirmedRequestPreservesJsonTypesAndUsesDocumentedFields()
    {
        string parameters = Path.GetTempFileName(), variables = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(parameters, """{"count":3,"enabled":true,"nested":{"items":["one","two"]}}""");
            await File.WriteAllTextAsync(variables, """{"password":{"value":"secret-input","isSecret":true}}""");
            using var handler = new CapturingHandler();
            var result = await RunAsync(["--confirm", Confirmation, "--ref", "refs/heads/main", "--parameters-file", parameters, "--variables-file", variables], handler);
            Assert.AreEqual(0, result.Exit, result.Output);
            Assert.AreEqual(1, handler.Calls);
            Assert.AreEqual("https://dev.azure.com/example/Backend%20Project/_apis/pipelines/12/runs?api-version=7.1", handler.Uri);
            using var body = JsonDocument.Parse(handler.Body!);
            var root = body.RootElement;
            Assert.AreEqual(3, root.GetProperty("templateParameters").GetProperty("count").GetInt32());
            Assert.IsTrue(root.GetProperty("templateParameters").GetProperty("enabled").GetBoolean());
            Assert.AreEqual("two", root.GetProperty("templateParameters").GetProperty("nested").GetProperty("items")[1].GetString());
            Assert.AreEqual("refs/heads/main", root.GetProperty("resources").GetProperty("repositories").GetProperty("self").GetProperty("refName").GetString());
            Assert.AreEqual("secret-input", root.GetProperty("variables").GetProperty("password").GetProperty("value").GetString());
            Assert.IsTrue(root.GetProperty("variables").GetProperty("password").GetProperty("isSecret").GetBoolean());
            Assert.IsFalse(root.TryGetProperty("runtimeParameters", out _));
            Assert.IsFalse(root.TryGetProperty("previewRun", out _));
            using var output = JsonDocument.Parse(result.Output);
            Assert.AreEqual(34, output.RootElement.GetProperty("data").GetProperty("id").GetInt32());
            Assert.IsFalse(result.Output.Contains("secret-input", StringComparison.Ordinal));
        }
        finally { File.Delete(parameters); File.Delete(variables); }
    }

    [TestMethod]
    [DataRow("--parameters-file", "[]")]
    [DataRow("--parameters-file", "{\"key\":1,\"key\":2}")]
    [DataRow("--parameters-file", "{\"nested\":{\"key\":1,\"key\":2}}")]
    [DataRow("--parameters-file", "{\"secret-sentinel\":")]
    [DataRow("--variables-file", "{\"key\":\"secret-sentinel\"}")]
    [DataRow("--variables-file", "{\"key\":{\"value\":123}}")]
    [DataRow("--variables-file", "{\"key\":{\"value\":\"secret-sentinel\",\"extra\":true}}")]
    [DataRow("--variables-file", "{\"key\":{\"value\":\"secret-sentinel\",\"isSecret\":\"yes\"}}")]
    public async Task MalformedInputsFailSafelyBeforeAnyNetwork(string flag, string content)
    {
        string path = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(path, content);
            using var handler = new TransportTests.FakeHandler(_ => throw new AssertFailedException());
            var result = await RunAsync(["--confirm", Confirmation, flag, path], handler);
            Assert.AreEqual(2, result.Exit, result.Output);
            Assert.IsFalse(result.Output.Contains("secret-sentinel", StringComparison.Ordinal));
            Assert.AreEqual(0, handler.Calls);
        }
        finally { File.Delete(path); }
    }

    [TestMethod]
    [DataRow('a', 256 * 1024)]
    [DataRow('<', 200 * 1024)]
    public async Task OversizedInputsOrSerializedRequestsAreRejectedEvenForDryRun(char character, int count)
    {
        string path = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(path, "{\"large\":\"" + new string(character, count) + "\"}");
            using var handler = new TransportTests.FakeHandler(_ => throw new AssertFailedException());
            Assert.AreEqual(2, (await RunAsync(["--dry-run", "--parameters-file", path], handler)).Exit);
            Assert.AreEqual(0, handler.Calls);
        }
        finally { File.Delete(path); }
    }

    [TestMethod]
    public async Task AmbiguousSubmissionKeepsNonRetryableExitEightThroughCli()
    {
        using var handler = new TransportTests.FakeHandler(_ => throw new HttpRequestException("secret-sentinel"));
        var result = await RunAsync(["--confirm", Confirmation], handler);
        Assert.AreEqual(8, result.Exit, result.Output);
        using var json = JsonDocument.Parse(result.Output);
        var error = json.RootElement.GetProperty("error");
        Assert.AreEqual("uncertain_write", error.GetProperty("code").GetString());
        Assert.IsFalse(error.GetProperty("retryable").GetBoolean());
        Assert.IsFalse(result.Output.Contains("secret-sentinel", StringComparison.Ordinal));
        Assert.AreEqual(1, handler.Calls);
    }

    private sealed class CapturingHandler : HttpMessageHandler
    {
        public int Calls { get; private set; }
        public string? Body { get; private set; }
        public string? Uri { get; private set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            Assert.AreEqual(HttpMethod.Post, request.Method);
            Assert.AreEqual("Basic", request.Headers.Authorization!.Scheme);
            Uri = request.RequestUri!.AbsoluteUri;
            Body = await request.Content!.ReadAsStringAsync(cancellationToken);
            return TransportTests.Json("""{"id":34,"pipeline":{"id":12},"name":"secret-input","finalYaml":"secret-input","variables":{"password":{"value":"secret-input"}}}""");
        }
    }

    private static async Task<(int Exit, string Output)> RunAsync(string[] args, HttpMessageHandler handler, Func<string, string?>? environment = null)
    {
        string path = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(path, """{"defaultProfile":"test","profiles":{"test":{"organization":"example","project":"Backend Project"}}}""");
            using var output = new StringWriter();
            using var error = new StringWriter();
            int exit = await CliApp.RunAsync(["pipeline", "run", "start", "--pipeline-id", "12", .. args, "--config", path, "--json", "--non-interactive"], output, error,
                environment: environment ?? (key => key == "ADO_TOKEN" ? "synthetic" : null), testHandler: handler);
            return (exit, output.ToString());
        }
        finally { File.Delete(path); }
    }
}
