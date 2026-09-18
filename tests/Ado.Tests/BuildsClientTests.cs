using System.Text.Json;
using Ado.Application;
using Ado.Domain;
using Ado.Infrastructure;
using Ado.Infrastructure.Http;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Ado.Tests;

[TestClass]
public sealed class BuildsClientTests
{
    [TestMethod]
    public void EndpointsEncodeFiltersAndKeepBuildIdsSeparate()
    {
        var uri = EndpointBuilder.Build(Operations.BuildList, "example", "Backend Project", top: 10, continuation: "a+/=&b",
            filters: new(12, "completed", "failed", "refs/heads/a&b"));
        Assert.AreEqual("/example/Backend%20Project/_apis/build/builds", uri.AbsolutePath);
        foreach (string query in new[] { "api-version=7.1", "%24top=10", "definitions=12", "statusFilter=completed", "resultFilter=failed",
            "branchName=refs%2Fheads%2Fa%26b", "continuationToken=a%2B%2F%3D%26b", "queryOrder=queueTimeDescending" })
            StringAssert.Contains(uri.Query, query);
        Assert.AreEqual("https://dev.azure.com/example/Backend%20Project/_apis/build/builds/34?api-version=7.1",
            EndpointBuilder.Build(Operations.BuildGet, "example", "Backend Project", 34).AbsoluteUri);
        Assert.ThrowsExactly<AdoException>(() => EndpointBuilder.Build(Operations.BuildGet, "example", "Backend", 0));
        Assert.ThrowsExactly<AdoException>(() => EndpointBuilder.Build(Operations.BuildGet, "example", "Backend", 34, top: 1));
    }

    [TestMethod]
    public async Task OpaquePagesPreserveFiltersAndStopAtItemLimit()
    {
        int calls = 0;
        string token = new('a', 200);
        using var handler = new TransportTests.FakeHandler(request =>
        {
            calls++;
            Assert.AreEqual(HttpMethod.Get, request.Method);
            StringAssert.Contains(request.RequestUri!.Query, "definitions=12");
            if (calls == 2) StringAssert.Contains(request.RequestUri.Query, "continuationToken=" + token);
            var response = TransportTests.Json(JsonSerializer.Serialize(new { value = new[] { new { id = calls, definition = new { id = 12 } } } }));
            response.Headers.Add("x-ms-continuationtoken", calls == 1 ? token : "next");
            return response;
        });
        using var http = new HttpClient(handler);
        using var auth = new TokenAuthentication("pat", new("synthetic"));
        var result = await new BuildsClient(new(http, auth, "example"), "example", "Backend").ListAsync(1, 2, null, new(12), CancellationToken.None);
        Assert.AreEqual(2, result.Items.Count);
        Assert.AreEqual("next", result.Meta.ContinuationToken);
        Assert.AreEqual("item_limit", result.Meta.TruncationReason);
        Assert.AreEqual(2, calls);
    }

    [TestMethod]
    public async Task GetAllowsOnlySafeFieldsAndRedactsCredentials()
    {
        using var handler = new TransportTests.FakeHandler(_ => TransportTests.Json("""{"id":34,"definition":{"id":12,"name":"Backend synthetic"},"project":{"name":"Backend"},"buildNumber":"20260917.1","status":"completed","result":"partiallySucceeded","queueTime":"2026-09-17T12:00:00Z","sourceBranch":"refs/heads/main","parameters":"secret-sentinel","requestedBy":{"displayName":"secret-sentinel"},"repository":{"url":"secret-sentinel"},"url":"secret-sentinel"}"""));
        using var http = new HttpClient(handler);
        using var auth = new TokenAuthentication("pat", new("synthetic"));
        var result = await new BuildsClient(new(http, auth, "example"), "example", "Backend").GetAsync(34, CancellationToken.None);
        Assert.AreEqual("Backend [REDACTED]", result.Items[0].DefinitionName);
        Assert.AreEqual("partiallySucceeded", result.Items[0].Result);
        Assert.IsNotNull(result.Items[0].QueueTime);
        Assert.IsNull(result.Items[0].FinishTime);
        Assert.IsFalse(JsonSerializer.Serialize(result).Contains("secret-sentinel", StringComparison.Ordinal));
    }

    [TestMethod]
    [DataRow("{\"id\":35,\"definition\":{\"id\":12}}")]
    [DataRow("{\"id\":34,\"definition\":{\"id\":0}}")]
    [DataRow("{\"id\":34,\"definition\":{\"id\":12},\"queueTime\":\"invalid\"}")]
    [DataRow("{\"id\":34,\"definition\":{\"id\":12},\"project\":{\"name\":\"Other\"}}")]
    public async Task MalformedOrMismatchedBuildIsRejected(string content)
    {
        using var handler = new TransportTests.FakeHandler(_ => TransportTests.Json(content));
        using var http = new HttpClient(handler);
        using var auth = new TokenAuthentication("pat", new("synthetic"));
        var client = new BuildsClient(new(http, auth, "example"), "example", "Backend");
        var error = await Assert.ThrowsExactlyAsync<AdoException>(() => client.GetAsync(34, CancellationToken.None));
        Assert.AreEqual("invalid_service_response", error.Code);
    }

    [TestMethod]
    public async Task RepeatedContinuationFailsInsteadOfLooping()
    {
        using var handler = new TransportTests.FakeHandler(_ =>
        {
            var response = TransportTests.Json("{\"value\":[]}");
            response.Headers.Add("x-ms-continuationtoken", "same");
            return response;
        });
        using var http = new HttpClient(handler);
        using var auth = new TokenAuthentication("pat", new("synthetic"));
        var client = new BuildsClient(new(http, auth, "example"), "example", "Backend");
        await Assert.ThrowsExactlyAsync<AdoException>(() => client.ListAsync(1, 100, null, new(), CancellationToken.None));
        Assert.AreEqual(2, handler.Calls);
    }

    [TestMethod]
    public async Task EmptyPagesStillRespectPageCeiling()
    {
        int calls = 0;
        using var handler = new TransportTests.FakeHandler(_ =>
        {
            var response = TransportTests.Json("{\"value\":[]}");
            response.Headers.Add("x-ms-continuationtoken", (++calls).ToString(System.Globalization.CultureInfo.InvariantCulture));
            return response;
        });
        using var http = new HttpClient(handler);
        using var auth = new TokenAuthentication("pat", new("synthetic"));
        var result = await new BuildsClient(new(http, auth, "example"), "example", "Backend").ListAsync(1, 100, null, new(), CancellationToken.None);
        Assert.AreEqual("page_limit", result.Meta.TruncationReason);
        Assert.AreEqual(100, calls);
    }
}
