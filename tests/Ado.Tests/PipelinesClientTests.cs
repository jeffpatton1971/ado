using System.Text.Json;
using Ado.Application;
using Ado.Domain;
using Ado.Infrastructure;
using Ado.Infrastructure.Http;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Ado.Tests;

[TestClass]
public sealed class PipelinesClientTests
{
    [TestMethod]
    public async Task PipelinePagingUsesOpaqueTokensAndReturnsContinuationAtLimit()
    {
        int calls = 0;
        using var handler = new TransportTests.FakeHandler(request =>
        {
            if (++calls == 2) StringAssert.Contains(request.RequestUri!.Query, "continuationToken=next%2Bpage");
            var response = TransportTests.Json("""{"value":[{"id":12,"name":"Backend","configuration":{"type":"yaml"}}]}""");
            response.Headers.Add("x-ms-continuationtoken", calls == 1 ? "next+page" : "last/page");
            return response;
        });
        using var client = new HttpClient(handler);
        using var auth = new TokenAuthentication("pat", new("token"));
        var result = await new PipelinesClient(new(client, auth, "example"), "example", "project").ListAsync(1, 2, null, CancellationToken.None);
        Assert.AreEqual(2, result.Items.Count);
        Assert.AreEqual("last/page", result.Meta.ContinuationToken);
        Assert.IsTrue(result.Meta.Truncated);
    }

    [TestMethod]
    public async Task RunsHaveClientLimitWithoutInventedServerPagingOrSensitiveFields()
    {
        using var handler = new TransportTests.FakeHandler(request =>
        {
            Assert.AreEqual("?api-version=7.1", request.RequestUri!.Query);
            return TransportTests.Json("""{"value":[{"id":1,"name":"run","pipeline":{"id":12},"variables":{"password":{"value":"secret-sentinel"}},"finalYaml":"secret-sentinel"},{"id":2,"pipeline":{"id":12}}]}""");
        });
        using var client = new HttpClient(handler);
        using var auth = new TokenAuthentication("pat", new("token"));
        var result = await new PipelinesClient(new(client, auth, "example"), "example", "project").RunsAsync(12, 1, CancellationToken.None);
        Assert.AreEqual(1, result.Items.Count);
        Assert.IsTrue(result.Meta.Truncated);
        Assert.IsNull(result.Meta.ContinuationToken);
        Assert.IsFalse(JsonSerializer.Serialize(result).Contains("secret-sentinel", StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task RunServerCeilingReportsUnknownCompleteness()
    {
        string run = "{\"id\":1,\"pipeline\":{\"id\":12}}";
        using var handler = new TransportTests.FakeHandler(_ => TransportTests.Json("[" + string.Join(',', Enumerable.Repeat(run, 10000)) + "]"));
        using var client = new HttpClient(handler);
        using var auth = new TokenAuthentication("pat", new("token"));
        var result = await new PipelinesClient(new(client, auth, "example"), "example", "project").RunsAsync(12, 10000, CancellationToken.None);
        Assert.AreEqual("unknown", result.Meta.Completeness);
        Assert.AreEqual("server_limit", result.Meta.TruncationReason);
        Assert.IsNull(result.Meta.ContinuationToken);
    }

    [TestMethod]
    public async Task RunFromDifferentPipelineIsRejected()
    {
        using var handler = new TransportTests.FakeHandler(_ => TransportTests.Json("""{"id":34,"pipeline":{"id":99}}"""));
        using var client = new HttpClient(handler);
        using var auth = new TokenAuthentication("pat", new("token"));
        var error = await Assert.ThrowsExactlyAsync<AdoException>(() => new PipelinesClient(new(client, auth, "example"), "example", "project").RunGetAsync(12, 34, CancellationToken.None));
        Assert.AreEqual("invalid_service_response", error.Code);
    }

    [TestMethod]
    public async Task RepeatedContinuationFailsInsteadOfLooping()
    {
        using var handler = new TransportTests.FakeHandler(_ =>
        {
            var response = TransportTests.Json("{\"value\":[]}");
            response.Headers.Add("x-ms-continuationtoken", "repeat");
            return response;
        });
        using var client = new HttpClient(handler);
        using var auth = new TokenAuthentication("pat", new("token"));
        await Assert.ThrowsExactlyAsync<Ado.Domain.AdoException>(() => new PipelinesClient(new(client, auth, "example"), "example", "project").ListAsync(1, 100, null, CancellationToken.None));
        Assert.AreEqual(2, handler.Calls);
    }
}
