using System.Net;
using System.Text.Json;
using Ado.Application;
using Ado.Cli;
using Ado.Domain;
using Ado.Infrastructure;
using Ado.Infrastructure.Http;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Ado.Tests;

[TestClass]
public sealed class BuildTimelineHistoryTests
{
    private static readonly Guid Current = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid Prior = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly Guid RecordId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
    private static string Body(Guid timeline, int attempt, Guid? details = null, bool history = false) => JsonSerializer.Serialize(new
    {
        id = timeline,
        records = new[] { new { id = RecordId, name = "Task", attempt, type = "Task", result = attempt == 1 ? "failed" : "succeeded",
            details = details is null ? null : new { id = details, url = "https://evil.invalid/secret-sentinel" },
            previousAttempts = history ? new[] { new { attempt = 1, recordId = RecordId, timelineId = Prior } } : [] } }
    });

    [TestMethod]
    public async Task LoadsHistoryOnceAndKeepsSameRecordIdInDifferentTimelines()
    {
        using var handler = new TransportTests.FakeHandler(request =>
        {
            Assert.AreEqual(HttpMethod.Get, request.Method);
            Assert.AreEqual("dev.azure.com", request.RequestUri!.Host);
            Assert.IsNotNull(request.Headers.Authorization);
            if (request.RequestUri.AbsolutePath.EndsWith("/timeline", StringComparison.Ordinal))
                return TransportTests.Json(Body(Current, 2, Prior, true));
            Assert.AreEqual($"/example/Project/_apis/build/builds/34/timeline/{Prior:D}", request.RequestUri.AbsolutePath);
            return TransportTests.Json(Body(Prior, 1, Current)); // Cycle is loaded once, not recursively repeated.
        });
        using var http = new HttpClient(handler);
        using var auth = new TokenAuthentication("pat", new("synthetic"));
        var result = await new BuildTimelineClient(new ServiceTransport(http, auth, "example"), "example", "Project")
            .GetAsync(34, 100, CancellationToken.None, true);
        Assert.AreEqual(2, handler.Calls);
        Assert.AreEqual(2, result.Items.Count);
        Assert.AreEqual(Current, result.Items[0].TimelineId);
        Assert.AreEqual(Prior, result.Items[1].TimelineId);
        Assert.AreEqual("complete", result.Meta.Completeness);
        Assert.IsFalse(JsonSerializer.Serialize(result).Contains("secret-sentinel", StringComparison.Ordinal));
    }

    [TestMethod]
    [DataRow("missingRecord", "attempt_references_unresolved")]
    [DataRow("wrongAttempt", "attempt_references_unresolved")]
    [DataRow("notFound", "referenced_timeline_unavailable")]
    public async Task UnresolvedHistoryPreservesRecords(string mode, string reason)
    {
        using var handler = new TransportTests.FakeHandler(request => request.RequestUri!.AbsolutePath.EndsWith("/timeline", StringComparison.Ordinal)
            ? TransportTests.Json(Body(Current, 2, history: true))
            : mode == "notFound" ? new HttpResponseMessage(HttpStatusCode.NotFound)
            : TransportTests.Json(mode == "missingRecord" ? JsonSerializer.Serialize(new { id = Prior, records = Array.Empty<object>() }) : Body(Prior, 3)));
        using var http = new HttpClient(handler);
        using var auth = new TokenAuthentication("pat", new("synthetic"));
        var result = await new BuildTimelineClient(new ServiceTransport(http, auth, "example"), "example", "Project")
            .GetAsync(34, 100, CancellationToken.None, true);
        Assert.IsTrue(result.Items.Count >= 1);
        Assert.AreEqual("unknown", result.Meta.Completeness);
        Assert.AreEqual(reason, result.Meta.TruncationReason);
    }

    [TestMethod]
    public async Task RefusesMismatchedTimelineIdentity()
    {
        using var handler = new TransportTests.FakeHandler(_ => TransportTests.Json(Body(Current, 2, Prior, true)));
        using var http = new HttpClient(handler);
        using var auth = new TokenAuthentication("pat", new("synthetic"));
        var error = await Assert.ThrowsExactlyAsync<AdoException>(() => new BuildTimelineClient(new ServiceTransport(http, auth, "example"), "example", "Project")
            .GetAsync(34, 100, CancellationToken.None, true));
        Assert.AreEqual("invalid_service_response", error.Code);
        Assert.AreEqual(2, handler.Calls);
    }

    [TestMethod]
    public async Task TraversalHasATimelineCeiling()
    {
        int count = 0;
        Guid Identity(int n) => new(n, 0, 0, new byte[8]);
        using var handler = new TransportTests.FakeHandler(_ => TransportTests.Json(Body(Identity(++count), 1, Identity(count + 1))));
        using var http = new HttpClient(handler);
        using var auth = new TokenAuthentication("pat", new("synthetic"));
        var result = await new BuildTimelineClient(new ServiceTransport(http, auth, "example"), "example", "Project")
            .GetAsync(34, 1000, CancellationToken.None, true);
        Assert.AreEqual(100, handler.Calls);
        Assert.AreEqual(100, result.Items.Count);
        Assert.AreEqual("partial", result.Meta.Completeness);
        Assert.AreEqual("timeline_limit", result.Meta.TruncationReason);
    }

    [TestMethod]
    public async Task TraversalReservesResponseBytesBeforeFollowingMoreReferences()
    {
        int count = 0;
        Guid Identity(int n) => new(n, 0, 0, new byte[8]);
        using var handler = new TransportTests.FakeHandler(_ => TransportTests.Json(
            Body(Identity(++count), 1, Identity(count + 1)).PadRight(4 * 1024 * 1024 - 128)));
        using var http = new HttpClient(handler);
        using var auth = new TokenAuthentication("pat", new("synthetic"));
        var result = await new BuildTimelineClient(new ServiceTransport(http, auth, "example"), "example", "Project")
            .GetAsync(34, 1000, CancellationToken.None, true);
        Assert.AreEqual(16, handler.Calls);
        Assert.AreEqual("byte_limit", result.Meta.TruncationReason);
        Assert.AreEqual("partial", result.Meta.Completeness);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task StrictItemLimitStopsBeforeExtraRequest(bool json)
    {
        using var handler = new TransportTests.FakeHandler(_ => TransportTests.Json(Body(Current, 2, Prior, true)));
        using var output = new StringWriter();
        using var error = new StringWriter();
        int exit = await CliApp.RunAsync(["build", "timeline", "--build-id", "34", "--organization", "example", "--project", "Project",
            "--include-history", "--limit", "1", "--require-complete", "--read-only", "--non-interactive", "--output", json ? "json" : "table"],
            output, error, environment: key => key == "ADO_TOKEN" ? "synthetic" : null, testHandler: handler);
        Assert.AreEqual(10, exit, output.ToString());
        Assert.AreEqual(1, handler.Calls);
        StringAssert.Contains(output.ToString(), Current.ToString());
        if (json)
        {
            using var document = JsonDocument.Parse(output.ToString());
            Assert.AreEqual("item_limit", document.RootElement.GetProperty("meta").GetProperty("truncationReason").GetString());
        }
        else StringAssert.Contains(output.ToString(), "TIMELINE ID");
    }
}
