using System.IO.Compression;
using System.Text.Json;
using Ado.Cli;
using Ado.Domain;
using Ado.Infrastructure;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Ado.Tests;

[TestClass]
public sealed class ArtifactEvidenceTests
{
    private static string Zip(string directory)
    {
        string path = Path.Combine(directory, "private-source.zip");
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        using var writer = new StreamWriter(zip.CreateEntry("plugin.json").Open());
        writer.Write("raw-content-secret-sentinel");
        return path;
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task ExportUsesAllowlistedFieldsAndMarksUnverifiedOrigin(bool dryRun)
    {
        string directory = Directory.CreateTempSubdirectory("ado-evidence-").FullName;
        try
        {
            string zip = Zip(directory), destination = Path.Combine(directory, "evidence.json");
            using var handler = new TransportTests.FakeHandler(_ => throw new AssertFailedException("Network access"));
            using var output = new StringWriter(); using var error = new StringWriter();
            string[] extra = dryRun ? ["--dry-run"] : [];
            int exit = await CliApp.RunAsync(["artifact", "evidence", "--file", zip, "--entry", "plugin.json", "--destination", destination,
                "--organization", "example", "--project", "Project", "--build-id", "34", "--artifact-name", "CompiledOutputs",
                "--json", "--non-interactive", "--read-only", .. extra], output, error,
                environment: key => key == "ADO_CONFIG" ? "missing-config.json" : null, testHandler: handler);
            Assert.AreEqual(0, exit, output.ToString());
            Assert.AreEqual(0, handler.Calls);
            Assert.AreEqual("", error.ToString());
            Assert.AreEqual(!dryRun, File.Exists(destination));
            if (!dryRun && !OperatingSystem.IsWindows()) Assert.AreEqual(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(destination));
            using var result = JsonDocument.Parse(output.ToString());
            string manifest = dryRun ? result.RootElement.GetProperty("data").GetProperty("evidence").GetRawText() : await File.ReadAllTextAsync(destination);
            Assert.IsFalse(manifest.Contains("raw-content-secret-sentinel", StringComparison.Ordinal));
            Assert.IsFalse(manifest.Contains("private-source.zip", StringComparison.Ordinal));
            Assert.IsFalse(manifest.Contains(directory, StringComparison.Ordinal));
            using var evidence = JsonDocument.Parse(manifest);
            Assert.AreEqual(1, evidence.RootElement.GetProperty("schemaVersion").GetInt32());
            Assert.AreEqual("user_supplied_unverified", evidence.RootElement.GetProperty("originVerification").GetString());
            Assert.AreEqual(34, evidence.RootElement.GetProperty("claimedOrigin").GetProperty("buildId").GetInt32());
            Assert.AreEqual("plugin.json", evidence.RootElement.GetProperty("member").GetProperty("path").GetString());
            Assert.AreEqual(64, evidence.RootElement.GetProperty("member").GetProperty("sha256").GetString()!.Length);
            Assert.AreEqual(dryRun ? 1 : 2, Directory.GetFiles(directory).Length);
        }
        finally { Directory.Delete(directory, true); }
    }

    [TestMethod]
    public async Task LocalEvidenceDoesNotInventOriginAndCannotOverwrite()
    {
        string directory = Directory.CreateTempSubdirectory("ado-evidence-").FullName;
        try
        {
            string zip = Zip(directory), destination = Path.Combine(directory, "evidence.json");
            var first = await ArtifactEvidenceExporter.ExportAsync(zip, "plugin.json", destination, null, null, false, CancellationToken.None);
            Assert.IsNull(first.Evidence.ClaimedOrigin);
            Assert.AreEqual("not_supplied", first.Evidence.OriginVerification);
            string original = await File.ReadAllTextAsync(destination);
            var error = await Assert.ThrowsExactlyAsync<AdoException>(() => ArtifactEvidenceExporter.ExportAsync(zip, "plugin.json", destination, null, null, false, CancellationToken.None));
            Assert.AreEqual("destination_exists", error.Code);
            Assert.AreEqual(original, await File.ReadAllTextAsync(destination));
            Assert.AreEqual(0, Directory.GetFiles(directory, ".ado-*.partial").Length);
        }
        finally { Directory.Delete(directory, true); }
    }

    [TestMethod]
    [DataRow("missing")]
    [DataRow("hash")]
    [DataRow("origin")]
    public async Task InvalidEvidenceRequestsPublishNothing(string mode)
    {
        string directory = Directory.CreateTempSubdirectory("ado-evidence-").FullName;
        try
        {
            string zip = Zip(directory), destination = Path.Combine(directory, "evidence.json");
            await Assert.ThrowsExactlyAsync<AdoException>(() => ArtifactEvidenceExporter.ExportAsync(zip, mode == "missing" ? "absent" : "plugin.json",
                destination, mode == "hash" ? new string('0', 64) : null,
                mode == "origin" ? new("example", "Project", null, "Artifact") : null, false, CancellationToken.None));
            Assert.IsFalse(File.Exists(destination));
            Assert.AreEqual(1, Directory.GetFiles(directory).Length);
        }
        finally { Directory.Delete(directory, true); }
    }
}
