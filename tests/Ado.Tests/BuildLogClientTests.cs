using System.Text.Json;
using Ado.Application;
using Ado.Cli;
using Ado.Domain;
using Ado.Infrastructure;
using Ado.Infrastructure.Http;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Ado.Tests;

[TestClass]
public sealed class BuildLogClientTests
{
    [TestMethod]
    public void RoutesEncodeProjectAndUseInt64LinePositions()
    {
        Assert.AreEqual("https://dev.azure.com/example/My%20Project/_apis/build/builds/34/logs?api-version=7.1",
            EndpointBuilder.BuildLog(Operations.BuildLogs, "example", "My Project", 34).AbsoluteUri);
        Assert.AreEqual("https://dev.azure.com/example/My%20Project/_apis/build/builds/34/logs/2?api-version=7.1&startLine=3000000000&endLine=3000000100",
            EndpointBuilder.BuildLog(Operations.BuildLogGet, "example", "My Project", 34, 2, 3000000000, 3000000100).AbsoluteUri);
        Assert.ThrowsExactly<AdoException>(() => EndpointBuilder.BuildLog(Operations.BuildLogs, "example", "My Project", 34, startLine: 1));
    }

    [TestMethod]
    public async Task IndexOmitsUrlsAndSupportsLongLineCountsAndLocalLimit()
    {
        using var handler = new TransportTests.FakeHandler(_ => TransportTests.Json("""{"value":[{"id":2,"lineCount":3000000000,"type":"Container","url":"secret-sentinel","createdOn":"2026-09-17T12:00:00Z"},{"id":3,"lineCount":0}]}"""));
        using var http = new HttpClient(handler);
        using var auth = new TokenAuthentication("pat", new("synthetic"));
        var result = await new BuildLogsClient(new(http, auth, "example"), "example", "Backend").ListAsync(34, 1, CancellationToken.None);
        Assert.AreEqual(3000000000L, result.Items[0].LineCount);
        Assert.IsTrue(result.Meta.Truncated);
        Assert.IsNull(result.Meta.ContinuationToken);
        Assert.IsFalse(JsonSerializer.Serialize(result).Contains("secret-sentinel", StringComparison.Ordinal));
    }

    [TestMethod]
    [DataRow("\"first\\r\\nsecond\\n\"")]
    [DataRow("[\"first\",\"second\"]")]
    [DataRow("{\"count\":2,\"value\":[\"first\",\"second\"]}")]
    public async Task NegotiatedJsonLogFormsAreBounded(string content)
    {
        using var handler = new TransportTests.FakeHandler(request =>
        {
            Assert.AreEqual("application/json", request.Headers.Accept.Single().MediaType);
            Assert.AreEqual(HttpMethod.Get, request.Method);
            Assert.AreEqual("/example/Backend/_apis/build/builds/34/logs/2", request.RequestUri!.AbsolutePath);
            return TransportTests.Json(content);
        });
        using var http = new HttpClient(handler);
        using var auth = new TokenAuthentication("pat", new("synthetic"));
        var result = await new BuildLogsClient(new(http, auth, "example"), "example", "Backend").GetAsync(34, 2, null, null, 1, CancellationToken.None);
        CollectionAssert.AreEqual(new[] { "first" }, result.Data.Lines.ToArray());
        Assert.AreEqual("line_limit", result.Meta.TruncationReason);
        Assert.AreEqual(2, result.Meta.ScannedCount);
    }

    [TestMethod]
    [DataRow("\"\"")]
    [DataRow("[]")]
    public async Task EmptyWholeLogIsComplete(string content)
    {
        using var handler = new TransportTests.FakeHandler(_ => TransportTests.Json(content));
        using var http = new HttpClient(handler);
        using var auth = new TokenAuthentication("pat", new("synthetic"));
        var result = await new BuildLogsClient(new(http, auth, "example"), "example", "Backend").GetAsync(34, 2, null, null, 100, CancellationToken.None);
        Assert.AreEqual(0, result.Data.Lines.Count);
        Assert.AreEqual("complete", result.Meta.Completeness);
        Assert.IsFalse(result.Meta.Truncated);
    }

    [TestMethod]
    [DataRow("[{\"id\":2,\"lineCount\":-1}]")]
    [DataRow("[{\"id\":2},{\"id\":2}]")]
    [DataRow("[{\"id\":0}]")]
    [DataRow("[{\"id\":2,\"createdOn\":\"invalid\"}]")]
    public async Task MalformedIndexIsRejected(string content)
    {
        using var handler = new TransportTests.FakeHandler(_ => TransportTests.Json(content));
        using var http = new HttpClient(handler);
        using var auth = new TokenAuthentication("pat", new("synthetic"));
        var client = new BuildLogsClient(new(http, auth, "example"), "example", "Backend");
        var error = await Assert.ThrowsExactlyAsync<AdoException>(() => client.ListAsync(34, 100, CancellationToken.None));
        Assert.AreEqual("invalid_service_response", error.Code);
    }
}
