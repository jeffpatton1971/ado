using System.Net;
using Ado.Application;
using Ado.Domain;
using Ado.Infrastructure;
using Ado.Infrastructure.Http;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Ado.Tests;

[TestClass]
public sealed class TransportTests
{
    [TestMethod]
    public void EndpointsEncodeProjectAndPinVersion()
    {
        var uri = EndpointBuilder.Project(Operations.ProjectGet, "example", "Example Project");
        Assert.AreEqual("https://dev.azure.com/example/_apis/projects/Example%20Project?api-version=7.1", uri.AbsoluteUri);
        Assert.AreEqual("vsrm.dev.azure.com", EndpointBuilder.Host(ServiceHost.Release));
        Assert.AreEqual("feeds.dev.azure.com", EndpointBuilder.Host(ServiceHost.Feeds));
    }

    [TestMethod]
    [DataRow("https://evil.example/example/_apis/projects")]
    [DataRow("http://dev.azure.com/example/_apis/projects")]
    [DataRow("https://dev.azure.com/other/_apis/projects")]
    [DataRow("https://dev.azure.com:444/example/_apis/projects")]
    [DataRow("https://user@dev.azure.com/example/_apis/projects")]
    public async Task UnsafeDestinationsNeverReachHandler(string destination)
    {
        using var handler = new FakeHandler(_ => throw new AssertFailedException("Must not send"));
        using var client = new HttpClient(handler);
        using var auth = new TokenAuthentication("pat", new("test-token"));
        var transport = new ServiceTransport(client, auth, "example");
        var error = await Assert.ThrowsExactlyAsync<AdoException>(() => transport.GetAsync(Operations.ProjectList, new(destination), CancellationToken.None));
        Assert.AreEqual("unsafe_destination", error.Code);
        Assert.AreEqual(0, handler.Calls);
    }

    [TestMethod]
    public async Task RedirectIsRefusedWithoutFollowingOrLeakingResponse()
    {
        using var handler = new FakeHandler(_ => new(HttpStatusCode.Redirect)
        {
            Headers = { Location = new("https://evil.example/secret-sentinel") },
            Content = new StringContent("secret-sentinel")
        });
        using var client = new HttpClient(handler);
        using var auth = new TokenAuthentication("pat", new("secret-sentinel"));
        var transport = new ServiceTransport(client, auth, "example");
        var error = await Assert.ThrowsExactlyAsync<AdoException>(() => transport.GetAsync(Operations.ProjectList, EndpointBuilder.Project(Operations.ProjectList, "example"), CancellationToken.None));
        Assert.AreEqual("redirect_refused", error.Code);
        Assert.AreEqual(1, handler.Calls);
        Assert.IsFalse(error.Message.Contains("secret-sentinel", StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task ReadRetriesAreBoundedAndHonorRetryAfter()
    {
        using var handler = new FakeHandler(_ => new(HttpStatusCode.TooManyRequests) { Headers = { RetryAfter = new(TimeSpan.FromSeconds(2)) } });
        using var client = new HttpClient(handler);
        using var auth = new TokenAuthentication("pat", new("token"));
        var delays = new List<TimeSpan>();
        var transport = new ServiceTransport(client, auth, "example", delay: (wait, _) => { delays.Add(wait); return Task.CompletedTask; });
        var error = await Assert.ThrowsExactlyAsync<AdoException>(() => transport.GetAsync(Operations.ProjectList, EndpointBuilder.Project(Operations.ProjectList, "example"), CancellationToken.None));
        Assert.AreEqual(ExitCode.Transient, error.ExitCode);
        Assert.AreEqual(3, handler.Calls);
        CollectionAssert.AreEqual(new[] { TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(2) }, delays);
    }

    [TestMethod]
    [DataRow(401, 4)]
    [DataRow(403, 5)]
    [DataRow(404, 6)]
    [DataRow(409, 7)]
    public async Task HttpFailuresHaveStableExitCodesAndSafeMessages(int status, int exit)
    {
        using var handler = new FakeHandler(_ => new((HttpStatusCode)status) { Content = new StringContent("secret-sentinel") });
        using var client = new HttpClient(handler);
        using var auth = new TokenAuthentication("pat", new("secret-sentinel"));
        var transport = new ServiceTransport(client, auth, "example");
        var error = await Assert.ThrowsExactlyAsync<AdoException>(() => transport.GetAsync(Operations.ProjectList, EndpointBuilder.Project(Operations.ProjectList, "example"), CancellationToken.None));
        Assert.AreEqual(exit, (int)error.ExitCode);
        Assert.IsFalse(error.Message.Contains("secret-sentinel", StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task HeaderContinuationIsReturnedAtItemCeiling()
    {
        using var handler = new FakeHandler(request =>
        {
            Assert.IsTrue(request.RequestUri!.Query.Contains("%24top=1", StringComparison.Ordinal));
            var response = Json("""{"value":[{"id":"00000000-0000-0000-0000-000000000001","name":"first"}]}""");
            response.Headers.Add("x-ms-continuationtoken", "1");
            return response;
        });
        using var client = new HttpClient(handler);
        using var auth = new TokenAuthentication("pat", new("token"));
        var result = await new ProjectsClient(new(client, auth, "example"), "example").ListAsync(100, 1, null, null, CancellationToken.None);
        Assert.IsTrue(result.Meta.Truncated);
        Assert.AreEqual("1", result.Meta.ContinuationToken);
        Assert.AreEqual("partial", result.Meta.Completeness);
    }

    [TestMethod]
    public async Task SearchTraversesPagesAndRedactsReturnedToken()
    {
        int count = 0;
        using var handler = new FakeHandler(request =>
        {
            count++;
            var response = Json("""{"value":[{"id":"00000000-0000-0000-0000-000000000001","name":"match secret-sentinel"}]}""");
            if (count == 1) response.Headers.Add("x-ms-continuationtoken", "1");
            else StringAssert.Contains(request.RequestUri!.Query, "continuationToken=1");
            return response;
        });
        using var client = new HttpClient(handler);
        using var auth = new TokenAuthentication("pat", new("secret-sentinel"));
        var result = await new ProjectsClient(new(client, auth, "example"), "example").ListAsync(1, 10, null, "match", CancellationToken.None);
        Assert.AreEqual(2, result.Projects.Count);
        Assert.AreEqual("match [REDACTED]", result.Projects[0].Name);
        Assert.IsFalse(result.Meta.Truncated);
    }

    [TestMethod]
    public async Task WriteDescriptorsCannotUseReadTransport()
    {
        using var handler = new FakeHandler(_ => throw new AssertFailedException());
        using var client = new HttpClient(handler);
        using var auth = new TokenAuthentication("pat", new("token"));
        var write = Operations.ProjectList with { IsWrite = true };
        var transport = new ServiceTransport(client, auth, "example", readOnly: true);
        var error = await Assert.ThrowsExactlyAsync<AdoException>(() => transport.GetAsync(write, EndpointBuilder.Project(Operations.ProjectList, "example"), CancellationToken.None));
        Assert.AreEqual("read_only_refusal", error.Code);
        Assert.AreEqual(0, handler.Calls);
    }

    internal static HttpResponseMessage Json(string content) => new(HttpStatusCode.OK) { Content = new StringContent(content, System.Text.Encoding.UTF8, "application/json") };

    [TestMethod]
    public async Task OversizedResponseFailsBeforeReadingContent()
    {
        using var handler = new FakeHandler(_ =>
        {
            var response = Json("{}");
            response.Content.Headers.ContentLength = 5 * 1024 * 1024;
            return response;
        });
        using var client = new HttpClient(handler);
        using var auth = new TokenAuthentication("pat", new("token"));
        var transport = new ServiceTransport(client, auth, "example");
        var error = await Assert.ThrowsExactlyAsync<AdoException>(() => transport.GetAsync(Operations.ProjectList, EndpointBuilder.Project(Operations.ProjectList, "example"), CancellationToken.None));
        Assert.AreEqual("response_limit_exceeded", error.Code);
    }

    [TestMethod]
    public async Task OversizedContinuationCannotMasqueradeAsCompleteResult()
    {
        using var handler = new FakeHandler(_ =>
        {
            var response = Json("{\"value\":[]}");
            response.Headers.Add("x-ms-continuationtoken", new string('1', 129));
            return response;
        });
        using var client = new HttpClient(handler);
        using var auth = new TokenAuthentication("pat", new("token"));
        var projects = new ProjectsClient(new(client, auth, "example"), "example");
        var error = await Assert.ThrowsExactlyAsync<AdoException>(() => projects.ListAsync(100, 100, null, null, CancellationToken.None));
        Assert.AreEqual("invalid_service_response", error.Code);
    }

    [TestMethod]
    public async Task RequestDeadlineCancelsHandlerAndMapsTimeout()
    {
        using var client = new HttpClient(new WaitingHandler());
        using var auth = new TokenAuthentication("pat", new("token"));
        var transport = new ServiceTransport(client, auth, "example", requestSeconds: 1);
        var error = await Assert.ThrowsExactlyAsync<AdoException>(() => transport.GetAsync(Operations.ProjectList, EndpointBuilder.Project(Operations.ProjectList, "example"), CancellationToken.None));
        Assert.AreEqual("request_timeout", error.Code);
    }

    private sealed class WaitingHandler : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            throw new AssertFailedException("Deadline must cancel the handler.");
        }
    }
    internal sealed class FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public int Calls { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(respond(request));
        }
    }
}
