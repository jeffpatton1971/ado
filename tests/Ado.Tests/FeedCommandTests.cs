using System.Net;
using System.Text.Json;
using Ado.Cli;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Ado.Tests;

[TestClass]
public sealed class FeedCommandTests
{
    private const string Id = "346e12cd-29da-48fb-9e85-ec354e24cbb6";
    private const string ProjectId = "1eb126bc-7c99-4a2d-ac2b-77db1b2134da";

    [TestMethod]
    [DataRow("list")]
    [DataRow("get")]
    public async Task MissingProjectNameCannotConfirmNameScopedFeed(string command)
    {
        string feed = JsonSerializer.Serialize(new { id = Id, name = "packages", project = new { id = ProjectId } });
        using var handler = new TransportTests.FakeHandler(_ => TransportTests.Json(command == "get" ? feed : "{\"value\":[" + feed + "]}"));
        var result = await Run([command, .. (command == "get" ? new[] { "--feed", "packages" } : Array.Empty<string>())], handler);
        Assert.AreEqual(9, result.Exit, result.Output);
    }
    private static string Feed(bool project = true, string name = "packages") => JsonSerializer.Serialize(new
    {
        id = Id,
        name,
        project = project ? new { id = ProjectId, name = "Backend Project" } : null,
        url = "secret-sentinel",
        upstreamSources = new[] { new { location = "secret-sentinel" } },
        permissions = new[] { new { displayName = "secret-sentinel" } }
    });

    [TestMethod]
    [DataRow("list", "project")]
    [DataRow("get", "project")]
    [DataRow("list", "organization")]
    [DataRow("get", "organization")]
    public async Task UsesScopedFeedHostAndAllowlistedOutput(string command, string scope)
    {
        using var handler = new TransportTests.FakeHandler(request =>
        {
            Assert.AreEqual(HttpMethod.Get, request.Method);
            Assert.AreEqual("feeds.dev.azure.com", request.RequestUri!.Host);
            Assert.AreEqual("/example" + (scope == "project" ? "/Backend%20Project" : "") + "/_apis/packaging/feeds" + (command == "get" ? "/packages" : ""), request.RequestUri.AbsolutePath);
            Assert.AreEqual("?api-version=7.1", request.RequestUri.Query);
            Assert.IsNotNull(request.Headers.Authorization);
            var body = Feed(scope == "project");
            return TransportTests.Json(command == "get" ? body : "{\"value\":[" + body + "]}");
        });
        var result = await Run([command, "--scope", scope, .. (command == "get" ? new[] { "--feed", "packages" } : Array.Empty<string>())], handler);
        Assert.AreEqual(0, result.Exit, result.Output);
        Assert.IsFalse(result.Output.Contains("secret-sentinel", StringComparison.Ordinal));
        using var json = JsonDocument.Parse(result.Output);
        var data = json.RootElement.GetProperty("data");
        var feed = command == "get" ? data : data[0];
        Assert.AreEqual(Id, feed.GetProperty("id").GetString());
        Assert.AreEqual(scope, feed.GetProperty("scope").GetString());
        Assert.AreEqual(scope == "project" ? "Backend Project" : null, json.RootElement.GetProperty("meta").GetProperty("project").GetString());
        Assert.AreEqual(1, handler.Calls);
    }

    [TestMethod]
    public async Task TruncationRetainsDataWithNoInventedPaging()
    {
        using var handler = new TransportTests.FakeHandler(_ => TransportTests.Json("{\"value\":[" + Feed() + "," + Feed(name: "second").Replace(Id, "223534ff-4e60-4279-b1b1-2790223b9061", StringComparison.Ordinal) + "]}"));
        var result = await Run(["list", "--limit", "1", "--require-complete"], handler);
        Assert.AreEqual(10, result.Exit, result.Output);
        using var json = JsonDocument.Parse(result.Output);
        Assert.AreEqual(1, json.RootElement.GetProperty("data").GetArrayLength());
        Assert.AreEqual(2, json.RootElement.GetProperty("meta").GetProperty("scannedCount").GetInt32());
        Assert.AreEqual(JsonValueKind.Null, json.RootElement.GetProperty("meta").GetProperty("continuationToken").ValueKind);
    }

    [TestMethod]
    [DataRow("get")]
    [DataRow("get", "--feed", "../other")]
    [DataRow("get", "--feed", "packages@view")]
    [DataRow("list", "--scope", "invalid")]
    [DataRow("list", "--top", "1")]
    [DataRow("list", "--continuation-token", "next")]
    public async Task InvalidOptionsFailBeforeCredentialLookup(params string[] args)
    {
        using var handler = new TransportTests.FakeHandler(_ => throw new AssertFailedException());
        var result = await Run(args, handler, token: false);
        Assert.AreEqual(2, result.Exit, result.Output);
        Assert.AreEqual(0, handler.Calls);
    }

    [TestMethod]
    [DataRow(401, 4)]
    [DataRow(403, 5)]
    [DataRow(404, 6)]
    public async Task ServiceFailuresRemainErrors(int status, int expectedExit)
    {
        using var handler = new TransportTests.FakeHandler(_ => new((HttpStatusCode)status) { Content = new StringContent("secret-sentinel") });
        var result = await Run(["get", "--feed", "packages"], handler);
        Assert.AreEqual(expectedExit, result.Exit, result.Output);
        Assert.IsFalse(result.Output.Contains("secret-sentinel", StringComparison.Ordinal));
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task WrongFeedOrProjectIsRejected(bool wrongProject)
    {
        using var handler = new TransportTests.FakeHandler(_ => TransportTests.Json(wrongProject
            ? Feed().Replace("Backend Project", "Other", StringComparison.Ordinal) : Feed(name: "other")));
        var result = await Run(["get", "--feed", "packages"], handler);
        Assert.AreEqual(9, result.Exit, result.Output);
        StringAssert.Contains(result.Output, "invalid_service_response");
    }

    [TestMethod]
    public async Task OrganizationListingRetainsProjectAssociationAndRedactsNames()
    {
        using var handler = new TransportTests.FakeHandler(_ => TransportTests.Json("{\"value\":[" + Feed(name: "synthetic\u001b[31m") + "]}"));
        var result = await Run(["list", "--scope", "organization"], handler, json: false);
        Assert.AreEqual(0, result.Exit, result.Output);
        StringAssert.Contains(result.Output, "[REDACTED]\\u001b[31m  project  Backend Project");
        Assert.IsFalse(result.Output.Contains('\u001b'));
    }

    [TestMethod]
    [DataRow("empty")]
    [DataRow("duplicate")]
    [DataRow("continuation")]
    [DataRow("malformed")]
    public async Task EmptyAndInvalidListsKeepHonestCompleteness(string scenario)
    {
        using var handler = new TransportTests.FakeHandler(_ =>
        {
            string body = scenario switch
            {
                "duplicate" => "{\"value\":[" + Feed() + "," + Feed() + "]}",
                "malformed" => "{\"value\":[{\"id\":123}]}",
                _ => "{\"value\":[]}"
            };
            var response = TransportTests.Json(body);
            if (scenario == "continuation") response.Headers.Add("x-ms-continuationtoken", "unexpected");
            return response;
        });
        var result = await Run(["list"], handler);
        Assert.AreEqual(scenario == "empty" ? 0 : 9, result.Exit, result.Output);
        if (scenario == "empty")
        {
            using var json = JsonDocument.Parse(result.Output);
            Assert.AreEqual(0, json.RootElement.GetProperty("data").GetArrayLength());
            Assert.AreEqual("complete", json.RootElement.GetProperty("meta").GetProperty("completeness").GetString());
        }
    }

    private static async Task<(int Exit, string Output)> Run(string[] args, HttpMessageHandler handler, bool token = true, bool json = true)
    {
        string path = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(path, """{"defaultProfile":"test","profiles":{"test":{"organization":"example","project":"Backend Project"}}}""");
            using var output = new StringWriter();
            using var error = new StringWriter();
            int exit = await CliApp.RunAsync(["feed", .. args, "--config", path, "--output", json ? "json" : "table", "--read-only", "--non-interactive"], output, error,
                environment: key => key == "ADO_TOKEN" && token ? "synthetic" : null, testHandler: handler);
            return (exit, output.ToString());
        }
        finally { File.Delete(path); }
    }
}
