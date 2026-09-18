using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using Ado.Cli;
using Ado.Domain;
using Ado.Infrastructure.Http;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Ado.Tests;

[TestClass]
public sealed class PackageDownloadTests
{
    private const string PackageId = "dbf8aa43-f7eb-4930-ab0b-ce1c2c63c7c4";
    private const string VersionId = "ceab6e7b-cf28-418e-bd70-b681ddc5cdce";

    [TestMethod]
    public void ContentEndpointPreservesProjectScopeAndRejectsDotSegments()
    {
        var uri = EndpointBuilder.NuGetContent("example", "Backend Project", "automation", "Core", "1.1.0-beta+build");
        Assert.AreEqual("pkgs.dev.azure.com", uri.Host);
        Assert.AreEqual("/example/Backend%20Project/_apis/packaging/feeds/automation/nuget/packages/Core/versions/1.1.0-beta%2Bbuild/content", uri.AbsolutePath);
        Assert.ThrowsExactly<AdoException>(() => EndpointBuilder.NuGetContent("example", null, "automation", "..", "1.0.0"));
        Assert.ThrowsExactly<AdoException>(() => EndpointBuilder.NuGetContent("example", null, "automation", "Core", ".."));
    }

    [TestMethod]
    [DataRow("direct", 0)]
    [DataRow("redirect", 0)]
    [DataRow("unsafe", 7)]
    [DataRow("oversize", 10)]
    [DataRow("not_zip", 9)]
    [DataRow("forbidden", 5)]
    public async Task ExactDownloadHashesBytesAndIsolatesCredentials(string scenario, int expectedExit)
    {
        string directory = Directory.CreateTempSubdirectory("ado-nuget-").FullName;
        try
        {
            using var memory = new MemoryStream();
            using (var zip = new ZipArchive(memory, ZipArchiveMode.Create, true))
            using (var writer = new StreamWriter(zip.CreateEntry("Core.nuspec").Open()))
                writer.Write("<package><metadata><id>Core</id><version>1.1.0</version><description>raw-content-sentinel</description><dependencies><group targetFramework='net9.0'><dependency id='Dependency' version='[2.0,3.0)'/></group></dependencies></metadata></package>");
            byte[] bytes = scenario == "not_zip" ? new byte[32] : memory.ToArray();
            int listCalls = 0, storageCalls = 0;
            using var handler = new TransportTests.FakeHandler(request =>
            {
                Assert.AreEqual(HttpMethod.Get, request.Method);
                if (request.RequestUri!.Host == "feeds.dev.azure.com")
                {
                    Assert.IsNotNull(request.Headers.Authorization);
                    if (request.RequestUri.AbsolutePath.EndsWith("/packages", StringComparison.Ordinal))
                        return TransportTests.Json(++listCalls == 1 ? "{\"value\":[{\"id\":\"" + PackageId + "\",\"name\":\"Core\",\"protocolType\":\"NuGet\"}]}" : "{\"value\":[]}");
                    return TransportTests.Json("{\"value\":[{\"id\":\"" + VersionId + "\",\"version\":\"1.1.0\",\"normalizedVersion\":\"1.1.0\",\"isDeleted\":false}]}");
                }
                if (request.RequestUri.Host == "pkgs.dev.azure.com")
                {
                    Assert.AreEqual("/example/_apis/packaging/feeds/automation/nuget/packages/Core/versions/1.1.0/content", request.RequestUri.AbsolutePath);
                    Assert.AreEqual("?api-version=7.1-preview.1", request.RequestUri.Query);
                    Assert.IsNotNull(request.Headers.Authorization);
                    if (scenario == "forbidden") return new(HttpStatusCode.Forbidden);
                    if (scenario is "redirect" or "unsafe")
                        return new(HttpStatusCode.Redirect) { Headers = { Location = new(scenario == "redirect" ? "https://test.vsblob.vsassets.io/file?sig=secret-sentinel" : "https://evil.example/file?sig=secret-sentinel") } };
                }
                else
                {
                    storageCalls++;
                    Assert.AreEqual("test.vsblob.vsassets.io", request.RequestUri.Host);
                    Assert.IsNull(request.Headers.Authorization);
                    Assert.IsFalse(request.Headers.Contains("Cookie"));
                }
                return new(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) { Headers = { ContentType = new("application/octet-stream") } } };
            });
            string destination = Path.Combine(directory, "Core.1.1.0.nupkg");
            var result = await Run(destination, scenario == "oversize" ? ["--max-bytes", "1"] : [], handler);
            Assert.AreEqual(expectedExit, result.Exit, result.Output);
            Assert.IsFalse(result.Output.Contains("secret-sentinel", StringComparison.Ordinal));
            Assert.AreEqual(scenario == "redirect" ? 1 : 0, storageCalls);
            if (expectedExit == 0)
            {
                CollectionAssert.AreEqual(bytes, await File.ReadAllBytesAsync(destination));
                using var json = JsonDocument.Parse(result.Output);
                var data = json.RootElement.GetProperty("data");
                Assert.AreEqual(Convert.ToHexStringLower(SHA256.HashData(bytes)), data.GetProperty("sha256").GetString());
                Assert.AreEqual(VersionId, data.GetProperty("resolution").GetProperty("version").GetProperty("id").GetString());
                Assert.AreEqual(1, Directory.GetFiles(directory).Length);
                await VerifyLocalWorkflow(data, directory);
            }
            else Assert.AreEqual(0, Directory.GetFiles(directory).Length);
        }
        finally { Directory.Delete(directory, true); }
    }

    private static async Task VerifyLocalWorkflow(JsonElement download, string directory)
    {
        // Consume the download's automation contract rather than reconstructing its output.
        string file = download.GetProperty("destination").GetString()!;
        string digest = download.GetProperty("sha256").GetString()!;
        using var handler = new TransportTests.FakeHandler(_ => throw new AssertFailedException("Local workflow made an HTTP request"));
        async Task<(int Exit, string Output)> Local(string[] args)
        {
            using var output = new StringWriter(); using var error = new StringWriter();
            int exit = await CliApp.RunAsync([.. args, "--file", file, "--json", "--read-only", "--non-interactive", "--config", Path.Combine(directory, "absent.json")],
                output, error, input: new StringReader(""), environment: _ => null, testHandler: handler);
            Assert.AreEqual("", error.ToString());
            Assert.IsFalse(output.ToString().Contains("raw-content-sentinel", StringComparison.Ordinal));
            return (exit, output.ToString());
        }
        var inspected = await Local(["package", "inspect", "--expected-sha256", digest]);
        Assert.AreEqual(0, inspected.Exit, inspected.Output);
        using var inspection = JsonDocument.Parse(inspected.Output);
        var package = inspection.RootElement.GetProperty("data");
        Assert.AreEqual(1, inspection.RootElement.GetProperty("meta").GetProperty("schemaVersion").GetInt32());
        Assert.AreEqual(digest, package.GetProperty("archiveSha256").GetString());
        Assert.AreEqual(download.GetProperty("resolution").GetProperty("package").GetProperty("name").GetString(), package.GetProperty("id").GetString());
        Assert.AreEqual(download.GetProperty("resolution").GetProperty("version").GetProperty("version").GetString(), package.GetProperty("version").GetString());
        string member = package.GetProperty("manifestPath").GetString()!;
        string memberHash = package.GetProperty("manifestSha256").GetString()!;
        string destination = Path.Combine(directory, "evidence.json");
        var exported = await Local(["artifact", "evidence", "--entry", member, "--expected-sha256", digest, "--destination", destination]);
        Assert.AreEqual(0, exported.Exit, exported.Output);
        using var evidence = JsonDocument.Parse(await File.ReadAllTextAsync(destination));
        Assert.AreEqual(digest, evidence.RootElement.GetProperty("archive").GetProperty("sha256").GetString());
        Assert.AreEqual(memberHash, evidence.RootElement.GetProperty("member").GetProperty("sha256").GetString());
        Assert.AreEqual("not_supplied", evidence.RootElement.GetProperty("originVerification").GetString());
        Assert.IsFalse(evidence.RootElement.GetRawText().Contains("raw-content-sentinel", StringComparison.Ordinal));
        Assert.IsFalse(evidence.RootElement.GetRawText().Contains(directory, StringComparison.Ordinal));

        // A stale/wrong expected digest must stop publication, even after a successful download.
        string rejectedDestination = Path.Combine(directory, "rejected.json");
        var rejected = await Local(["artifact", "evidence", "--entry", member, "--expected-sha256", new string('0', 64), "--destination", rejectedDestination]);
        Assert.AreEqual(7, rejected.Exit, rejected.Output);
        using var failure = JsonDocument.Parse(rejected.Output);
        Assert.AreEqual("archive_hash_mismatch", failure.RootElement.GetProperty("error").GetProperty("code").GetString());
        Assert.IsFalse(File.Exists(rejectedDestination));
        Assert.AreEqual(0, handler.Calls);
        Assert.AreEqual(2, Directory.GetFiles(directory).Length);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task DryRunNeedsNoCredentialsAndNeverOverwrites(bool exists)
    {
        string directory = Directory.CreateTempSubdirectory("ado-nuget-dry-").FullName;
        try
        {
            string path = Path.Combine(directory, "package.nupkg");
            if (exists) await File.WriteAllTextAsync(path, "original");
            using var handler = new TransportTests.FakeHandler(_ => throw new AssertFailedException());
            var result = await Run(path, ["--dry-run"], handler, false);
            Assert.AreEqual(exists ? 7 : 0, result.Exit, result.Output);
            Assert.AreEqual(0, handler.Calls);
            Assert.AreEqual(exists ? 1 : 0, Directory.GetFiles(directory).Length);
            if (exists) Assert.AreEqual("original", await File.ReadAllTextAsync(path));
        }
        finally { Directory.Delete(directory, true); }
    }

    private static async Task<(int Exit, string Output)> Run(string destination, string[] args, HttpMessageHandler handler, bool token = true)
    {
        string config = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(config, """{"defaultProfile":"test","profiles":{"test":{"organization":"example","project":"ignored"}}}""");
            using var output = new StringWriter();
            using var error = new StringWriter();
            int exit = await CliApp.RunAsync(["package", "download", "--scope", "organization", "--feed", "automation", "--name", "Core", "--package-version", "1.1.0",
                "--destination", destination, "--config", config, "--json", "--read-only", "--non-interactive", .. args], output, error,
                environment: key => key == "ADO_TOKEN" && token ? "synthetic" : null, testHandler: handler);
            return (exit, output.ToString());
        }
        finally { File.Delete(config); }
    }
}
