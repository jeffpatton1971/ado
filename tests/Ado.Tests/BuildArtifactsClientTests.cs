using System.Text.Json;
using Ado.Application;
using Ado.Domain;
using Ado.Infrastructure;
using Ado.Infrastructure.Http;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Ado.Tests;

[TestClass]
public sealed class BuildArtifactsClientTests
{
    [TestMethod]
    public void NameIsEncodedAsOneQueryValue()
    {
        var uri = EndpointBuilder.BuildArtifact(Operations.BuildArtifactGet, "example", "My Project", 34, "drop +/&x=1");
        Assert.AreEqual("https://dev.azure.com/example/My%20Project/_apis/build/builds/34/artifacts?api-version=7.1&artifactName=drop%20%2B%2F%26x%3D1", uri.AbsoluteUri);
        Assert.ThrowsExactly<AdoException>(() => EndpointBuilder.BuildArtifact(Operations.BuildArtifactGet, "example", "Project", 34, "\n"));
        Assert.ThrowsExactly<AdoException>(() => EndpointBuilder.BuildArtifact(Operations.BuildArtifactList, "example", "Project", 0));
    }

    [TestMethod]
    public async Task MetadataOmitsAllLinksAndPropertiesAndPreservesType()
    {
        using var handler = new TransportTests.FakeHandler(request =>
        {
            Assert.AreEqual(HttpMethod.Get, request.Method);
            Assert.AreEqual("application/json", request.Headers.Accept.Single().MediaType);
            return TransportTests.Json("""{"id":7,"name":"drop","source":"job synthetic","resource":{"type":"FutureType","data":"secret-sentinel","downloadUrl":"https://evil.invalid/?sig=secret-sentinel","url":"secret-sentinel","properties":{"token":"secret-sentinel"}},"_links":{"self":"secret-sentinel"}}""");
        });
        using var http = new HttpClient(handler);
        using var auth = new TokenAuthentication("pat", new("synthetic"));
        var result = await new BuildArtifactsClient(new(http, auth, "example"), "example", "Project").GetAsync(34, "drop", CancellationToken.None);
        Assert.AreEqual("FutureType", result.Items[0].ResourceType);
        Assert.AreEqual("job [REDACTED]", result.Items[0].Source);
        Assert.IsFalse(JsonSerializer.Serialize(result).Contains("secret-sentinel", StringComparison.Ordinal));
        Assert.AreEqual(1, handler.Calls);
    }

    [TestMethod]
    [DataRow("{\"id\":7,\"name\":\"other\",\"resource\":{}}")]
    [DataRow("{\"id\":0,\"name\":\"drop\",\"resource\":{}}")]
    [DataRow("{\"id\":7,\"name\":\"drop\",\"resource\":null}")]
    public async Task MalformedOrMismatchedArtifactIsRejected(string content)
    {
        using var handler = new TransportTests.FakeHandler(_ => TransportTests.Json(content));
        using var http = new HttpClient(handler);
        using var auth = new TokenAuthentication("pat", new("synthetic"));
        var client = new BuildArtifactsClient(new(http, auth, "example"), "example", "Project");
        var error = await Assert.ThrowsExactlyAsync<AdoException>(() => client.GetAsync(34, "drop", CancellationToken.None));
        Assert.AreEqual("invalid_service_response", error.Code);
    }

    [TestMethod]
    public async Task ListLimitIsLocalAndHasNoContinuation()
    {
        using var handler = new TransportTests.FakeHandler(request =>
        {
            Assert.AreEqual("?api-version=7.1", request.RequestUri!.Query);
            return TransportTests.Json("""{"value":[{"id":7,"name":"drop","resource":{}},{"id":8,"name":"symbols","resource":{"type":"Container"}}]}""");
        });
        using var http = new HttpClient(handler);
        using var auth = new TokenAuthentication("pat", new("synthetic"));
        var result = await new BuildArtifactsClient(new(http, auth, "example"), "example", "Project").ListAsync(34, 1, CancellationToken.None);
        Assert.AreEqual("unknown", result.Items[0].ResourceType);
        Assert.IsTrue(result.Meta.Truncated);
        Assert.IsNull(result.Meta.ContinuationToken);
        Assert.AreEqual("item_limit", result.Meta.TruncationReason);
    }

    [TestMethod]
    public async Task EmptyArtifactListIsSuccessful()
    {
        using var handler = new TransportTests.FakeHandler(_ => TransportTests.Json("{\"value\":[]}"));
        using var http = new HttpClient(handler);
        using var auth = new TokenAuthentication("pat", new("synthetic"));
        var result = await new BuildArtifactsClient(new(http, auth, "example"), "example", "Project").ListAsync(34, 100, CancellationToken.None);
        Assert.AreEqual(0, result.Items.Count);
        Assert.AreEqual("complete", result.Meta.Completeness);
    }
}
