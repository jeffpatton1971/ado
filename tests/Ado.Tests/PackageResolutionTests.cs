using System.Text.Json;
using Ado.Cli;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Ado.Tests;

[TestClass]
public sealed class PackageResolutionTests
{
    private const string PackageId = "dbf8aa43-f7eb-4930-ab0b-ce1c2c63c7c4";
    private const string VersionId = "ceab6e7b-cf28-418e-bd70-b681ddc5cdce";

    [TestMethod]
    [DataRow("found", 0)]
    [DataRow("similar_name", 6)]
    [DataRow("different_version", 6)]
    [DataRow("package_bound", 10)]
    [DataRow("version_bound", 10)]
    [DataRow("ambiguous_name", 9)]
    [DataRow("ambiguous_version", 9)]
    public async Task ResolutionNeverSubstitutesSimilarOrIncompleteEvidence(string scenario, int expected)
    {
        int packageCalls = 0, versionCalls = 0;
        using var handler = new TransportTests.FakeHandler(request =>
        {
            Assert.AreEqual(HttpMethod.Get, request.Method);
            Assert.AreEqual("feeds.dev.azure.com", request.RequestUri!.Host);
            if (request.RequestUri.AbsolutePath.EndsWith("/packages", StringComparison.Ordinal))
            {
                packageCalls++;
                StringAssert.Contains(request.RequestUri.Query, "protocolType=NuGet");
                StringAssert.Contains(request.RequestUri.Query, "packageNameQuery=Core");
                if (packageCalls > 1) return TransportTests.Json("{\"value\":[]}");
                var packages = new List<object> { new { id = PackageId, name = scenario == "similar_name" ? "Core.Extras" : "core", protocolType = "NuGet" } };
                if (scenario == "ambiguous_name") packages.Add(new { id = VersionId, name = "CORE", protocolType = "NuGet" });
                return TransportTests.Json(JsonSerializer.Serialize(new { value = packages }));
            }
            versionCalls++;
            Assert.AreEqual("/example/_apis/packaging/feeds/automation/packages/" + PackageId + "/versions", request.RequestUri.AbsolutePath);
            var versions = new List<object> { new { id = VersionId, version = scenario == "different_version" ? "1.2.30" : "1.2.3", isLatest = false, isDeleted = false } };
            if (scenario is "version_bound" or "ambiguous_version") versions.Add(new { id = PackageId, version = "1.2.3", isLatest = true });
            if (scenario == "version_bound") versions.Add(new { id = "95e29076-1e7a-430b-a6d1-c9dc7b3f7659", version = "3.0.0" });
            return TransportTests.Json(JsonSerializer.Serialize(new { value = versions }));
        });
        int limit = scenario == "package_bound" ? 1 : scenario == "version_bound" ? 2 : 100;
        var result = await Run(["--limit", limit.ToString(System.Globalization.CultureInfo.InvariantCulture)], handler);
        Assert.AreEqual(expected, result.Exit, result.Output);
        if (scenario == "found")
        {
            using var json = JsonDocument.Parse(result.Output);
            var data = json.RootElement.GetProperty("data");
            Assert.AreEqual(PackageId, data.GetProperty("package").GetProperty("id").GetString());
            Assert.AreEqual(VersionId, data.GetProperty("version").GetProperty("id").GetString());
            Assert.AreEqual("exact_name_and_literal_version", data.GetProperty("match").GetString());
            Assert.AreEqual(2, packageCalls);
            Assert.AreEqual(1, versionCalls);
        }
        if (scenario is "similar_name" or "package_bound" or "ambiguous_name") Assert.AreEqual(0, versionCalls);
    }

    [TestMethod]
    [DataRow("--package-version", "[1.0,2.0)")]
    [DataRow("--package-version", "*")]
    [DataRow("--name", "../Core")]
    public async Task InvalidSelectorsNeverRetrieveCredentials(string flag, string value)
    {
        using var handler = new TransportTests.FakeHandler(_ => throw new AssertFailedException());
        var result = await Run([flag, value], handler, token: false, replace: flag);
        Assert.AreEqual(2, result.Exit, result.Output);
        Assert.AreEqual(0, handler.Calls);
    }

    private static async Task<(int Exit, string Output)> Run(string[] args, HttpMessageHandler handler, bool token = true, string? replace = null)
    {
        string path = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(path, """{"defaultProfile":"test","profiles":{"test":{"organization":"example","project":"Backend"}}}""");
            using var output = new StringWriter();
            using var error = new StringWriter();
            int exit = await CliApp.RunAsync(["package", "resolve", "--feed", "automation", "--scope", "organization",
                .. (replace == "--name" ? Array.Empty<string>() : new[] { "--name", "Core" }),
                .. (replace == "--package-version" ? Array.Empty<string>() : new[] { "--package-version", "1.2.3" }),
                .. args, "--config", path, "--json", "--read-only", "--non-interactive"], output, error,
                environment: key => key == "ADO_TOKEN" && token ? "synthetic" : null, testHandler: handler);
            return (exit, output.ToString());
        }
        finally { File.Delete(path); }
    }
}
