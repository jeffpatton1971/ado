using System.Text.Json;
using Ado.Application;
using Ado.Cli;
using Ado.Domain;
using Ado.Infrastructure;
using Ado.Infrastructure.Http;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Ado.Tests;

[TestClass]
public sealed class ReleaseTests
{
    [TestMethod]
    public async Task EmptyListIsComplete()
    {
        using var handler = new TransportTests.FakeHandler(_ => TransportTests.Json("{\"value\":[]}"));
        using var http = new HttpClient(handler);
        using var auth = new TokenAuthentication("pat", new("synthetic"));
        var result = await new ReleasesClient(new ServiceTransport(http, auth, "example"), "example", "My Project")
            .ListAsync(20, 20, null, null, CancellationToken.None);
        Assert.AreEqual(0, result.Items.Count);
        Assert.AreEqual("complete", result.Meta.Completeness);
    }

    [TestMethod]
    [DataRow("get", "--release-id", "0")]
    [DataRow("list", "--definition-id", "-1")]
    [DataRow("list", "--continuation-token", "invalid")]
    public async Task CliRejectsInvalidInputBeforeCredentialLookup(string command, string option, string value)
    {
        using var handler = new TransportTests.FakeHandler(_ => throw new AssertFailedException());
        using var output = new StringWriter();
        using var error = new StringWriter();
        int exit = await CliApp.RunAsync(["release", command, option, value, "--organization", "example", "--project", "My Project", "--json", "--non-interactive", "--credential-provider", "environment"],
            output, error, environment: key => key == "ADO_TOKEN" ? throw new AssertFailedException("Token lookup") : null, testHandler: handler);
        Assert.AreEqual(2, exit, output.ToString());
        Assert.AreEqual(0, handler.Calls);
    }

    private static string Release(int id) => JsonSerializer.Serialize(new
    {
        id,
        name = "synthetic\u001b release",
        status = "active",
        releaseDefinition = new { id = 12, name = "Deploy" },
        projectReference = new { name = "My Project" },
        createdOn = "2026-09-17T00:00:00Z",
        variables = new { password = "secret-sentinel" },
        createdBy = new { displayName = "secret-sentinel" },
        environments = new[] { new { name = "secret-sentinel" } },
        url = "https://secret-sentinel"
    });

    [TestMethod]
    public async Task ListUsesReleaseHostAndResumesBoundedPages()
    {
        int calls = 0;
        using var handler = new TransportTests.FakeHandler(request =>
        {
            calls++;
            Assert.AreEqual("vsrm.dev.azure.com", request.RequestUri!.Host);
            Assert.IsNotNull(request.Headers.Authorization);
            Assert.IsTrue(request.RequestUri.Query.Contains("definitionId=12", StringComparison.Ordinal));
            if (calls == 2) Assert.IsTrue(request.RequestUri.Query.Contains("continuationToken=42", StringComparison.Ordinal));
            var response = TransportTests.Json("{\"value\":[" + Release(44 - calls) + "]}");
            response.Headers.Add("x-ms-continuationtoken", calls == 1 ? "42" : "41");
            return response;
        });
        using var http = new HttpClient(handler);
        using var auth = new TokenAuthentication("pat", new("synthetic"));
        var client = new ReleasesClient(new ServiceTransport(http, auth, "example"), "example", "My Project");
        var result = await client.ListAsync(1, 2, null, 12, CancellationToken.None);
        Assert.AreEqual(2, calls);
        Assert.AreEqual("41", result.Meta.ContinuationToken);
        Assert.AreEqual("partial", result.Meta.Completeness);
        Assert.AreEqual("[REDACTED]\u001b release", result.Items[0].Name);
        Assert.IsFalse(JsonSerializer.Serialize(result).Contains("secret-sentinel", StringComparison.Ordinal));
    }

    [TestMethod]
    [DataRow("abc")]
    [DataRow("-1")]
    [DataRow("2147483648")]
    [DataRow("1&extra=true")]
    public void RejectsInvalidContinuation(string token) => Assert.ThrowsExactly<AdoException>(() =>
        EndpointBuilder.Release(Operations.ReleaseList, "example", "My Project", continuation: token));

    [TestMethod]
    [DataRow("badToken")]
    [DataRow("repeatToken")]
    [DataRow("wrongDefinition")]
    [DataRow("wrongProject")]
    public async Task RejectsInconsistentServiceResponse(string mode)
    {
        using var handler = new TransportTests.FakeHandler(_ =>
        {
            var response = TransportTests.Json("{\"value\":[" + (mode == "wrongProject" ? Release(43).Replace("My Project", "Other", StringComparison.Ordinal) : Release(43)) + "]}");
            if (mode is "badToken" or "repeatToken") response.Headers.Add("x-ms-continuationtoken", mode == "badToken" ? "secret-sentinel" : "42");
            return response;
        });
        using var http = new HttpClient(handler);
        using var auth = new TokenAuthentication("pat", new("synthetic"));
        var error = await Assert.ThrowsExactlyAsync<AdoException>(() => new ReleasesClient(new ServiceTransport(http, auth, "example"), "example", "My Project")
            .ListAsync(1, 2, mode == "repeatToken" ? "42" : null, mode == "wrongDefinition" ? 13 : 12, CancellationToken.None));
        Assert.IsFalse(error.Message.Contains("secret-sentinel", StringComparison.Ordinal));
        Assert.AreEqual(1, handler.Calls);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task GetValidatesIdentity(bool mismatch)
    {
        using var handler = new TransportTests.FakeHandler(request =>
        {
            Assert.AreEqual("https://vsrm.dev.azure.com/example/My%20Project/_apis/release/releases/43?api-version=7.1", request.RequestUri!.AbsoluteUri);
            return TransportTests.Json(Release(mismatch ? 44 : 43));
        });
        using var http = new HttpClient(handler);
        using var auth = new TokenAuthentication("pat", new("synthetic"));
        var client = new ReleasesClient(new ServiceTransport(http, auth, "example"), "example", "My Project");
        if (mismatch) await Assert.ThrowsExactlyAsync<AdoException>(() => client.GetAsync(43, CancellationToken.None));
        else Assert.AreEqual(43, (await client.GetAsync(43, CancellationToken.None)).Items[0].Id);
    }

    [TestMethod]
    [DataRow("list", true)]
    [DataRow("get", false)]
    public async Task CliReadsSafeMetadata(string command, bool json)
    {
        using var handler = new TransportTests.FakeHandler(_ =>
        {
            var response = TransportTests.Json(command == "get" ? Release(43) : "{\"value\":[" + Release(43) + "]}");
            if (command == "list") response.Headers.Add("x-ms-continuationtoken", "42");
            return response;
        });
        using var output = new StringWriter();
        using var error = new StringWriter();
        string[] extra = command == "list" ? ["--limit", "1", "--require-complete", "--definition-id", "12"] : ["--release-id", "43"];
        int exit = await CliApp.RunAsync(["release", command, ..extra, "--organization", "example", "--project", "My Project",
            "--read-only", "--non-interactive", "--output", json ? "json" : "table"], output, error,
            environment: key => key == "ADO_TOKEN" ? "synthetic" : null, testHandler: handler);
        Assert.AreEqual(command == "list" ? 10 : 0, exit, output.ToString());
        Assert.IsFalse(output.ToString().Contains("secret-sentinel", StringComparison.Ordinal));
        Assert.IsFalse(output.ToString().Contains('\u001b'));
        if (json)
        {
            using var document = JsonDocument.Parse(output.ToString());
            Assert.AreEqual(1, document.RootElement.GetProperty("data").GetArrayLength());
        }
        else Assert.IsTrue(output.ToString().Contains("RELEASE ID", StringComparison.Ordinal));
    }
}
