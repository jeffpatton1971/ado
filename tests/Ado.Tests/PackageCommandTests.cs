using System.Net;
using System.Text.Json;
using Ado.Cli;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Ado.Tests;

[TestClass]
public sealed class PackageCommandTests
{
    private const string PackageId = "dbf8aa43-f7eb-4930-ab0b-ce1c2c63c7c4";
    private const string VersionId = "ceab6e7b-cf28-418e-bd70-b681ddc5cdce";
    private const string Version = """{"id":"ceab6e7b-cf28-418e-bd70-b681ddc5cdce","version":"1.2.3-beta.1","normalizedVersion":"1.2.3-beta.1","isListed":true,"isDeleted":false,"isLatest":false,"publishDate":"2026-09-18T12:00:00Z","author":"secret-sentinel","files":[{"url":"secret-sentinel"}]}""";
    private static string Package(string id = PackageId) => JsonSerializer.Serialize(new { id, name = "Core.synthetic", normalizedName = "core.synthetic", protocolType = "NuGet", url = "secret-sentinel", versions = new[] { new { version = "secret-sentinel" } } });

    [TestMethod]
    public async Task PackageOffsetResumesWithScopeAndFilters()
    {
        int calls = 0;
        using var handler = new TransportTests.FakeHandler(request =>
        {
            calls++;
            Assert.AreEqual(HttpMethod.Get, request.Method);
            Assert.AreEqual("feeds.dev.azure.com", request.RequestUri!.Host);
            Assert.AreEqual("/example/_apis/packaging/feeds/automation/packages", request.RequestUri.AbsolutePath);
            foreach (string filter in new[] { "protocolType=NuGet", "packageNameQuery=Core%26More", "includeAllVersions=false", "includeUrls=false", "includeDeleted=false" }) StringAssert.Contains(request.RequestUri.Query, filter);
            StringAssert.Contains(request.RequestUri.Query, "%24skip=" + (calls == 1 ? "0" : "1"));
            return TransportTests.Json(calls == 1 ? "{\"value\":[" + Package() + "]}" : "{\"value\":[]}");
        });
        string[] args = ["list", "--scope", "organization", "--protocol", "NuGet", "--name", "Core&More", "--limit", "1", "--require-complete"];
        var first = await Run(args, handler);
        Assert.AreEqual(10, first.Exit, first.Output);
        Assert.IsFalse(first.Output.Contains("secret-sentinel", StringComparison.Ordinal));
        using var json = JsonDocument.Parse(first.Output);
        Assert.AreEqual("1", json.RootElement.GetProperty("meta").GetProperty("continuationToken").GetString());
        Assert.AreEqual("Core.[REDACTED]", json.RootElement.GetProperty("data")[0].GetProperty("name").GetString());
        Assert.AreEqual(0, (await Run([.. args, "--continuation-token", "1"], handler)).Exit);
        Assert.AreEqual(2, calls);
    }

    [TestMethod]
    public async Task ShortPackagePageIsNotAssumedExhaustive()
    {
        int calls = 0;
        using var handler = new TransportTests.FakeHandler(_ => TransportTests.Json(++calls == 1 ? "{\"value\":[" + Package() + "]}" : "{\"value\":[]}"));
        var result = await Run(["list", "--limit", "10"], handler);
        Assert.AreEqual(0, result.Exit, result.Output);
        Assert.AreEqual(2, calls);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task VersionsAndExactVersionUseGuidRoutesWithoutSensitivePayloads(bool get)
    {
        using var handler = new TransportTests.FakeHandler(request =>
        {
            Assert.AreEqual("/example/Backend%20Project/_apis/packaging/feeds/automation/packages/" + PackageId + "/versions" + (get ? "/" + VersionId : ""), request.RequestUri!.AbsolutePath);
            Assert.AreEqual(HttpMethod.Get, request.Method);
            StringAssert.Contains(request.RequestUri.Query, "includeUrls=false");
            if (!get) StringAssert.Contains(request.RequestUri.Query, "isDeleted=false");
            return TransportTests.Json(get ? Version : "{\"value\":[" + Version + "]}");
        });
        var result = await Run([.. (get ? new[] { "version", "get", "--version-id", VersionId } : new[] { "versions" }), "--package-id", PackageId], handler);
        Assert.AreEqual(0, result.Exit, result.Output);
        Assert.IsFalse(result.Output.Contains("secret-sentinel", StringComparison.Ordinal));
        using var json = JsonDocument.Parse(result.Output);
        var data = json.RootElement.GetProperty("data");
        var item = get ? data : data[0];
        Assert.AreEqual("1.2.3-beta.1", item.GetProperty("version").GetString());
        Assert.AreEqual(PackageId, item.GetProperty("packageId").GetString());
        Assert.IsFalse(item.GetProperty("isLatest").GetBoolean());
    }

    [TestMethod]
    public async Task VersionListIsLocallyBoundedWithNoCursor()
    {
        using var handler = new TransportTests.FakeHandler(_ => TransportTests.Json("{\"value\":[" + Version + "," + Version.Replace(VersionId, "1f3e18d1-e14c-49e9-b5c6-a9f4e16bf736", StringComparison.Ordinal) + "]}"));
        var result = await Run(["versions", "--package-id", PackageId, "--limit", "1", "--require-complete"], handler);
        Assert.AreEqual(10, result.Exit, result.Output);
        using var json = JsonDocument.Parse(result.Output);
        Assert.AreEqual(2, json.RootElement.GetProperty("meta").GetProperty("scannedCount").GetInt32());
        Assert.AreEqual(JsonValueKind.Null, json.RootElement.GetProperty("meta").GetProperty("continuationToken").ValueKind);
    }

    [TestMethod]
    [DataRow("versions", "--package-id", "Core")]
    [DataRow("version", "get", "--package-id", PackageId, "--version-id", "1.2.3")]
    [DataRow("list", "--continuation-token", "-1")]
    [DataRow("list", "--scope", "invalid")]
    [DataRow("list", "--name", " ")]
    [DataRow("versions", "--package-id", PackageId, "--continuation-token", "1")]
    public async Task InvalidSelectorsFailBeforeAuthentication(params string[] args)
    {
        using var handler = new TransportTests.FakeHandler(_ => throw new AssertFailedException());
        var result = await Run(args, handler, token: false);
        Assert.AreEqual(2, result.Exit, result.Output);
        Assert.AreEqual(0, handler.Calls);
    }

    [TestMethod]
    public async Task WrongExactVersionCannotSilentlySatisfyRequest()
    {
        using var handler = new TransportTests.FakeHandler(_ => TransportTests.Json(Version.Replace(VersionId, PackageId, StringComparison.Ordinal)));
        Assert.AreEqual(9, (await Run(["version", "get", "--package-id", PackageId, "--version-id", VersionId], handler)).Exit);
    }

    [TestMethod]
    [DataRow(401, 4)]
    [DataRow(403, 5)]
    [DataRow(404, 6)]
    public async Task ServiceErrorsDoNotBecomeEmptySearches(int status, int exit)
    {
        using var handler = new TransportTests.FakeHandler(_ => new((HttpStatusCode)status) { Content = new StringContent("secret-sentinel") });
        var result = await Run(["list"], handler);
        Assert.AreEqual(exit, result.Exit, result.Output);
        Assert.IsFalse(result.Output.Contains("secret-sentinel", StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task RepeatedPackagePagesFailInsteadOfLooping()
    {
        using var handler = new TransportTests.FakeHandler(_ => TransportTests.Json("{\"value\":[" + Package() + "]}"));
        var result = await Run(["list", "--top", "1", "--limit", "10"], handler);
        Assert.AreEqual(9, result.Exit, result.Output);
        Assert.AreEqual(2, handler.Calls);
    }

    [TestMethod]
    public async Task VersionTableEscapesServiceControls()
    {
        using var handler = new TransportTests.FakeHandler(_ => TransportTests.Json(Version.Replace("1.2.3-beta.1", "1.2.3\\u001b[31m", StringComparison.Ordinal)));
        var result = await Run(["version", "get", "--package-id", PackageId, "--version-id", VersionId], handler, json: false);
        Assert.AreEqual(0, result.Exit, result.Output);
        StringAssert.Contains(result.Output, "1.2.3\\u001b[31m");
        Assert.IsFalse(result.Output.Contains('\u001b'));
    }

    private static async Task<(int Exit, string Output)> Run(string[] args, HttpMessageHandler handler, bool token = true, bool json = true)
    {
        string path = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(path, """{"defaultProfile":"test","profiles":{"test":{"organization":"example","project":"Backend Project"}}}""");
            using var output = new StringWriter();
            using var error = new StringWriter();
            int exit = await CliApp.RunAsync(["package", .. args, "--feed", "automation", "--config", path, "--output", json ? "json" : "table", "--read-only", "--non-interactive"], output, error,
                environment: key => key == "ADO_TOKEN" && token ? "synthetic" : null, testHandler: handler);
            return (exit, output.ToString());
        }
        finally { File.Delete(path); }
    }
}
