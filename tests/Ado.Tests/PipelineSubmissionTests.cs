using System.Net;
using Ado.Application;
using Ado.Domain;
using Ado.Infrastructure;
using Ado.Infrastructure.Http;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Ado.Tests;

[TestClass]
public sealed class PipelineSubmissionTests
{
    private const string Confirmation = "pipeline run start:example/Backend Project/12";

    [TestMethod]
    [DataRow(true, false, Confirmation, "read_only_refusal")]
    [DataRow(false, true, Confirmation, "dry_run_dispatch_refusal")]
    [DataRow(false, false, null, "confirmation_required")]
    [DataRow(false, false, "pipeline run start:example/Other/12", "confirmation_required")]
    public async Task DispatchGuardsPreventAnyRequest(bool readOnly, bool dryRun, string? confirmation, string code)
    {
        using var handler = new TransportTests.FakeHandler(_ => throw new AssertFailedException());
        using var http = new HttpClient(handler);
        using var auth = new TokenAuthentication("pat", new("synthetic"));
        var transport = new ServiceTransport(http, auth, "example", readOnly: readOnly, dryRun: dryRun);
        var error = await Assert.ThrowsExactlyAsync<AdoException>(() => transport.StartPipelineAsync("Backend Project", 12, "{}", confirmation, CancellationToken.None));
        Assert.AreEqual(code, error.Code);
        Assert.AreEqual(0, handler.Calls);
    }

    [TestMethod]
    [DataRow(408, 8)]
    [DataRow(429, 8)]
    [DataRow(500, 8)]
    [DataRow(503, 8)]
    [DataRow(401, 4)]
    [DataRow(403, 5)]
    [DataRow(409, 7)]
    [DataRow(302, 7)]
    public async Task WriteResponsesNeverRetryOrLeakBody(int status, int exit)
    {
        using var handler = new TransportTests.FakeHandler(_ => new((HttpStatusCode)status) { Content = new StringContent("secret-sentinel") });
        using var http = new HttpClient(handler);
        using var auth = new TokenAuthentication("pat", new("synthetic"));
        var transport = new ServiceTransport(http, auth, "example", readOnly: false);
        var error = await Assert.ThrowsExactlyAsync<AdoException>(() => transport.StartPipelineAsync("Backend Project", 12, "{}", Confirmation, CancellationToken.None));
        Assert.AreEqual(exit, (int)error.ExitCode);
        Assert.IsFalse(error.Retryable);
        Assert.IsFalse(error.Message.Contains("secret-sentinel", StringComparison.Ordinal));
        Assert.AreEqual(1, handler.Calls);
    }

    [TestMethod]
    [DataRow("not json")]
    [DataRow("{}")]
    [DataRow("{\"id\":34,\"pipeline\":{\"id\":13}}")]
    public async Task InvalidSuccessResponseIsAnUncertainWrite(string response)
    {
        using var handler = new TransportTests.FakeHandler(_ => TransportTests.Json(response));
        using var http = new HttpClient(handler);
        using var auth = new TokenAuthentication("pat", new("synthetic"));
        var client = new PipelinesClient(new(http, auth, "example", readOnly: false), "example", "Backend Project");
        var request = await PipelineRunRequest.LoadAsync(null, null, null, CancellationToken.None);
        var error = await Assert.ThrowsExactlyAsync<AdoException>(() => client.StartAsync(12, request, Confirmation, CancellationToken.None));
        Assert.AreEqual("uncertain_write", error.Code);
        Assert.AreEqual(1, handler.Calls);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task TransportFailureOrCancellationAfterDispatchIsUncertain(bool cancellation)
    {
        using var handler = new TransportTests.FakeHandler(_ => cancellation
            ? throw new OperationCanceledException() : throw new HttpRequestException("secret-sentinel"));
        using var http = new HttpClient(handler);
        using var auth = new TokenAuthentication("pat", new("synthetic"));
        var transport = new ServiceTransport(http, auth, "example", readOnly: false);
        var error = await Assert.ThrowsExactlyAsync<AdoException>(() => transport.StartPipelineAsync("Backend Project", 12, "{}", Confirmation, CancellationToken.None));
        Assert.AreEqual(ExitCode.UncertainWrite, error.ExitCode);
        Assert.AreEqual(1, handler.Calls);
    }

    [TestMethod]
    public async Task AlreadyCancelledSubmissionDoesNotDispatch()
    {
        using var handler = new TransportTests.FakeHandler(_ => throw new AssertFailedException());
        using var http = new HttpClient(handler);
        using var auth = new TokenAuthentication("pat", new("synthetic"));
        var transport = new ServiceTransport(http, auth, "example", readOnly: false);
        await Assert.ThrowsAsync<OperationCanceledException>(() => transport.StartPipelineAsync("Backend Project", 12, "{}", Confirmation, new CancellationToken(true)));
        Assert.AreEqual(0, handler.Calls);
    }
}
