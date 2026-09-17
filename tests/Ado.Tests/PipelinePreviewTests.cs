using System.Net;
using System.Text.Json;
using Ado.Application;
using Ado.Domain;
using Ado.Infrastructure;
using Ado.Infrastructure.Http;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Ado.Tests;

[TestClass]
public sealed class PipelinePreviewTests
{
    private const string Confirmation = "pipeline run preview:example/Sandbox/12";

    [TestMethod]
    public async Task PreviewAlwaysSendsTrueAndDoesNotChangeSubsequentStart()
    {
        using var handler = new CaptureHandler();
        using var http = new HttpClient(handler);
        using var auth = new TokenAuthentication("pat", new("synthetic-token"));
        var transport = new ServiceTransport(http, auth, "example", readOnly: false);
        var client = new PipelinesClient(transport, "example", "Sandbox");
        var request = await PipelineRunRequest.LoadAsync(null, null, "refs/heads/main", CancellationToken.None);
        var result = await client.PreviewAsync(12, request, Confirmation, false, CancellationToken.None);
        Assert.IsTrue(result.PreviewRun);
        Assert.IsNull(result.FinalYaml);
        Assert.AreEqual(30, result.YamlCharacters);
        using var body = JsonDocument.Parse(handler.Bodies[0]);
        Assert.IsTrue(body.RootElement.GetProperty("previewRun").GetBoolean());
        Assert.AreEqual("refs/heads/main", body.RootElement.GetProperty("resources").GetProperty("repositories").GetProperty("self").GetProperty("refName").GetString());
        Assert.AreEqual(34, await client.StartAsync(12, request, "pipeline run start:example/Sandbox/12", CancellationToken.None));
        using var startBody = JsonDocument.Parse(handler.Bodies[1]);
        Assert.IsFalse(startBody.RootElement.TryGetProperty("previewRun", out _));
    }

    [TestMethod]
    [DataRow(true, false, Confirmation, "read_only_refusal")]
    [DataRow(false, true, Confirmation, "dry_run_dispatch_refusal")]
    [DataRow(false, false, null, "confirmation_required")]
    [DataRow(false, false, "pipeline run start:example/Sandbox/12", "confirmation_required")]
    public async Task PreviewPolicyGuardsPreventDispatch(bool readOnly, bool dryRun, string? confirm, string code)
    {
        using var handler = new TransportTests.FakeHandler(_ => throw new AssertFailedException());
        using var http = new HttpClient(handler);
        using var auth = new TokenAuthentication("pat", new("synthetic-token"));
        var transport = new ServiceTransport(http, auth, "example", readOnly: readOnly, dryRun: dryRun);
        var request = await PipelineRunRequest.LoadAsync(null, null, null, CancellationToken.None);
        var error = await Assert.ThrowsExactlyAsync<AdoException>(() => transport.PreviewPipelineAsync("Sandbox", 12, request, confirm, CancellationToken.None));
        Assert.AreEqual(code, error.Code);
        Assert.AreEqual(0, handler.Calls);
    }

    [TestMethod]
    [DataRow(429, 9)]
    [DataRow(503, 9)]
    [DataRow(403, 5)]
    [DataRow(302, 7)]
    public async Task PreviewFailuresDoNotRetryOrClaimUncertainCreation(int status, int exit)
    {
        using var handler = new TransportTests.FakeHandler(_ => new((HttpStatusCode)status) { Content = new StringContent("secret-sentinel") });
        using var http = new HttpClient(handler);
        using var auth = new TokenAuthentication("pat", new("synthetic-token"));
        var transport = new ServiceTransport(http, auth, "example", readOnly: false);
        var request = await PipelineRunRequest.LoadAsync(null, null, null, CancellationToken.None);
        var error = await Assert.ThrowsExactlyAsync<AdoException>(() => transport.PreviewPipelineAsync("Sandbox", 12, request, Confirmation, CancellationToken.None));
        Assert.AreEqual(exit, (int)error.ExitCode);
        Assert.IsFalse(error.Retryable);
        Assert.IsFalse(error.Message.Contains("secret-sentinel", StringComparison.Ordinal));
        Assert.AreEqual(1, handler.Calls);
    }

    [TestMethod]
    [DataRow("{}")]
    [DataRow("{\"finalYaml\":123}")]
    [DataRow("{\"finalYaml\":\" \"}")]
    [DataRow("{\"finalYaml\":\"steps: []\",\"pipeline\":{\"id\":13}}")]
    public async Task MalformedPreviewFailsWithoutEchoingPayload(string content)
    {
        using var handler = new TransportTests.FakeHandler(_ => TransportTests.Json(content));
        using var http = new HttpClient(handler);
        using var auth = new TokenAuthentication("pat", new("synthetic-token"));
        var client = new PipelinesClient(new(http, auth, "example", readOnly: false), "example", "Sandbox");
        var request = await PipelineRunRequest.LoadAsync(null, null, null, CancellationToken.None);
        var error = await Assert.ThrowsExactlyAsync<AdoException>(() => client.PreviewAsync(12, request, Confirmation, false, CancellationToken.None));
        Assert.AreEqual("invalid_service_response", error.Code);
        Assert.AreEqual(1, handler.Calls);
    }

    [TestMethod]
    public async Task ExplicitYamlRedactsAuthenticationAndKnownInputStrings()
    {
        string path = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(path, """{"password":{"value":"private-input","isSecret":true}}""");
            using var handler = new TransportTests.FakeHandler(_ => TransportTests.Json("""{"finalYaml":"steps: [] # private-input synthetic-token"}"""));
            using var http = new HttpClient(handler);
            using var auth = new TokenAuthentication("pat", new("synthetic-token"));
            var client = new PipelinesClient(new(http, auth, "example", readOnly: false), "example", "Sandbox");
            var request = await PipelineRunRequest.LoadAsync(null, path, null, CancellationToken.None);
            var result = await client.PreviewAsync(12, request, Confirmation, true, CancellationToken.None);
            Assert.AreEqual("steps: [] # [REDACTED] [REDACTED]", result.FinalYaml);
        }
        finally { File.Delete(path); }
    }

    private sealed class CaptureHandler : HttpMessageHandler
    {
        public List<string> Bodies { get; } = [];
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Assert.AreEqual(HttpMethod.Post, request.Method);
            Assert.AreEqual("https://dev.azure.com/example/Sandbox/_apis/pipelines/12/runs?api-version=7.1", request.RequestUri!.AbsoluteUri);
            Bodies.Add(await request.Content!.ReadAsStringAsync(cancellationToken));
            return TransportTests.Json("""{"id":34,"pipeline":{"id":12},"finalYaml":"steps: [] # synthetic-token!!!"}""");
        }
    }
}
