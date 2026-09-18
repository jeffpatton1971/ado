using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using Ado.Cli;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Ado.Tests;

[TestClass]
public sealed class BuildArtifactEvidenceTests
{
    private static byte[] Zip()
    {
        using var memory = new MemoryStream();
        using (var zip = new ZipArchive(memory, ZipArchiveMode.Create, true))
        using (var writer = new StreamWriter(zip.CreateEntry("plugin.json").Open())) writer.Write("raw-content-secret");
        return memory.ToArray();
    }

    [TestMethod]
    [DataRow("Container", false)]
    [DataRow("PipelineArtifact", false)]
    [DataRow("Container", true)]
    public async Task DownloadsHashesAndExportsObservedIdentity(string type, bool missingEntry)
    {
        string directory = Directory.CreateTempSubdirectory("ado-service-evidence-").FullName;
        byte[] zip = Zip();
        try
        {
            using var handler = new TransportTests.FakeHandler(request =>
            {
                Assert.AreEqual(HttpMethod.Get, request.Method);
                if (request.RequestUri!.Host == "dev.azure.com")
                {
                    Assert.IsNotNull(request.Headers.Authorization);
                    if (request.RequestUri.AbsolutePath.EndsWith("/builds/34", StringComparison.Ordinal))
                        return TransportTests.Json("""{"id":34,"definition":{"id":12},"sourceVersion":"abc123","status":"completed","result":"succeeded"}""");
                    if (request.RequestUri.AbsolutePath.Contains("/pipelines/", StringComparison.Ordinal))
                        return TransportTests.Json("""{"name":"drop","signedContent":{"url":"https://test.artifacts.visualstudio.com/content?sig=secret-sentinel","signatureExpires":"2099-01-01T00:00:00Z"}}""");
                    if (request.Headers.Accept.Single().MediaType == "application/json")
                        return TransportTests.Json(JsonSerializer.Serialize(new { id = 7, name = "drop", resource = new { type, downloadUrl = "secret-sentinel" } }));
                    Assert.AreEqual("Container", type);
                }
                else
                {
                    Assert.AreEqual("test.artifacts.visualstudio.com", request.RequestUri.Host);
                    Assert.IsNull(request.Headers.Authorization);
                    Assert.IsFalse(request.Headers.Contains("Cookie"));
                }
                return new(HttpStatusCode.OK) { Content = new ByteArrayContent(zip) { Headers = { ContentType = new("application/zip") } } };
            });
            string target = Path.Combine(directory, "evidence.json");
            var result = await Run(target, missingEntry ? "absent" : "plugin.json", [], handler);
            Assert.AreEqual(missingEntry ? 6 : 0, result.Exit, result.Output);
            Assert.AreEqual(missingEntry ? 0 : 1, Directory.GetFiles(directory).Length);
            Assert.IsFalse(result.Output.Contains("secret-sentinel", StringComparison.Ordinal));
            Assert.IsFalse(result.Output.Contains("raw-content-secret", StringComparison.Ordinal));
            if (missingEntry) return;
            string evidenceText = await File.ReadAllTextAsync(target);
            Assert.IsFalse(evidenceText.Contains("raw-content-secret", StringComparison.Ordinal));
            Assert.IsFalse(evidenceText.Contains("secret-sentinel", StringComparison.Ordinal));
            using var evidence = JsonDocument.Parse(evidenceText);
            Assert.AreEqual("authenticated_metadata_and_download", evidence.RootElement.GetProperty("originVerification").GetString());
            Assert.AreEqual("abc123", evidence.RootElement.GetProperty("build").GetProperty("sourceVersion").GetString());
            Assert.AreEqual(7, evidence.RootElement.GetProperty("artifact").GetProperty("id").GetInt32());
            Assert.AreEqual(Convert.ToHexStringLower(SHA256.HashData(zip)), evidence.RootElement.GetProperty("archive").GetProperty("sha256").GetString());
        }
        finally { Directory.Delete(directory, true); }
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task DryRunAndExistingDestinationNeverDispatch(bool exists)
    {
        string directory = Directory.CreateTempSubdirectory("ado-service-evidence-").FullName;
        try
        {
            string target = Path.Combine(directory, "evidence.json");
            if (exists) await File.WriteAllTextAsync(target, "original");
            using var handler = new TransportTests.FakeHandler(_ => throw new AssertFailedException("HTTP"));
            var result = await Run(target, "plugin.json", ["--dry-run"], handler, noCredential: true);
            Assert.AreEqual(exists ? 7 : 0, result.Exit, result.Output);
            Assert.AreEqual(0, handler.Calls);
            Assert.AreEqual(exists ? 1 : 0, Directory.GetFiles(directory).Length);
            if (exists) Assert.AreEqual("original", await File.ReadAllTextAsync(target));
        }
        finally { Directory.Delete(directory, true); }
    }

    private static async Task<(int Exit, string Output)> Run(string target, string entry, string[] extra, HttpMessageHandler handler, bool noCredential = false)
    {
        using var output = new StringWriter(); using var error = new StringWriter();
        int exit = await CliApp.RunAsync(["build", "artifact", "evidence", "--build-id", "34", "--artifact-name", "drop", "--entry", entry,
            "--destination", target, "--organization", "example", "--project", "Project", "--json", "--non-interactive", "--read-only", .. extra],
            output, error, environment: key => key == "ADO_TOKEN" ? noCredential ? throw new AssertFailedException("Credential lookup") : "synthetic" : null, testHandler: handler);
        return (exit, output.ToString());
    }
}
