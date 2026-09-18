using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Ado.Cli;
using Ado.Domain;
using Ado.Infrastructure;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Ado.Tests;

[TestClass]
public sealed class ArtifactArchiveTests
{
    private static string Create(params string[] names)
    {
        string path = Path.Combine(Path.GetTempPath(), "ado-inspect-" + Guid.NewGuid().ToString("N") + ".zip");
        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
        foreach (string name in names)
        {
            var entry = archive.CreateEntry(name);
            using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
            writer.Write("test payload");
        }
        return path;
    }

    [TestMethod]
    public async Task SelectedMemberAndArchiveHashesAreVerifiedWithoutExtraction()
    {
        string path = Create("manifest.json", "reports/tests.xml");
        try
        {
            string expected = Convert.ToHexStringLower(SHA256.HashData(await File.ReadAllBytesAsync(path)));
            var result = await ArtifactArchiveInspector.InspectAsync(path, "manifest.json", expected, 100, CancellationToken.None);
            Assert.AreEqual(expected, result.Data.Sha256);
            Assert.AreEqual(true, result.Data.ExpectedHashMatches);
            Assert.AreEqual(2, result.Data.TotalEntries);
            Assert.AreEqual(1, result.Data.Entries.Count);
            Assert.AreEqual(Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes("test payload"))), result.Data.Entries[0].Sha256);
            Assert.AreEqual("complete", result.Meta.Completeness);
        }
        finally { File.Delete(path); }
    }

    [TestMethod]
    [DataRow("../outside")]
    [DataRow("/absolute")]
    [DataRow("C:/file")]
    [DataRow("a\\b")]
    [DataRow("a/./b")]
    [DataRow("a//b")]
    [DataRow("a\u001b")]
    public async Task UnsafeNamesAreRejectedEvenBeyondDisplayLimit(string name)
    {
        string path = Create("valid", name);
        try
        {
            var error = await Assert.ThrowsExactlyAsync<AdoException>(() => ArtifactArchiveInspector.InspectAsync(path, null, null, 1, CancellationToken.None));
            Assert.AreEqual("unsafe_archive", error.Code);
            Assert.IsFalse(error.Message.Contains(name, StringComparison.Ordinal));
        }
        finally { File.Delete(path); }
    }

    [TestMethod]
    [DataRow("a", "A")]
    [DataRow("a", "a/b")]
    [DataRow("a/b", "a")]
    public async Task PathCollisionsAreRejected(string first, string second)
    {
        string path = Create(first, second);
        try { Assert.AreEqual("unsafe_archive", (await Assert.ThrowsExactlyAsync<AdoException>(() => ArtifactArchiveInspector.InspectAsync(path, null, null, 100, CancellationToken.None))).Code); }
        finally { File.Delete(path); }
    }

    [TestMethod]
    public async Task WrongHashAndMissingSelectionFailSafely()
    {
        string path = Create("manifest.json");
        try
        {
            Assert.AreEqual("archive_hash_mismatch", (await Assert.ThrowsExactlyAsync<AdoException>(() => ArtifactArchiveInspector.InspectAsync(path, null, new string('0', 64), 100, CancellationToken.None))).Code);
            Assert.AreEqual("archive_entry_not_found", (await Assert.ThrowsExactlyAsync<AdoException>(() => ArtifactArchiveInspector.InspectAsync(path, "Manifest.json", null, 100, CancellationToken.None))).Code);
        }
        finally { File.Delete(path); }
    }

    [TestMethod]
    public async Task CliReturnsPartialJsonWithoutConfigCredentialsOrHttp()
    {
        string path = Create("first", "second");
        try
        {
            using var handler = new TransportTests.FakeHandler(_ => throw new AssertFailedException("Network access"));
            using var output = new StringWriter(); using var error = new StringWriter();
            int exit = await CliApp.RunAsync(["artifact", "inspect", "--file", path, "--limit", "1", "--require-complete", "--json", "--non-interactive", "--read-only"],
                output, error, environment: key => key == "ADO_CONFIG" ? "missing-config.json" : null, testHandler: handler);
            Assert.AreEqual(10, exit, output.ToString());
            Assert.AreEqual(0, handler.Calls);
            Assert.AreEqual("", error.ToString());
            using var result = JsonDocument.Parse(output.ToString());
            Assert.AreEqual(1, result.RootElement.GetProperty("data").GetProperty("entries").GetArrayLength());
            Assert.AreEqual("partial", result.RootElement.GetProperty("meta").GetProperty("completeness").GetString());
        }
        finally { File.Delete(path); }
    }

    [TestMethod]
    public async Task SymbolicLinkEntriesAndEntryCountCeilingAreRejected()
    {
        string path = Create("link");
        try
        {
            using (var archive = ZipFile.Open(path, ZipArchiveMode.Update)) archive.Entries[0].ExternalAttributes = unchecked((int)0xa1ff0000);
            Assert.AreEqual("unsafe_archive", (await Assert.ThrowsExactlyAsync<AdoException>(() => ArtifactArchiveInspector.InspectAsync(path, null, null, 100, CancellationToken.None))).Code);
            File.Delete(path);
            using (var archive = ZipFile.Open(path, ZipArchiveMode.Create))
                for (int index = 0; index < 10001; index++) archive.CreateEntry(index.ToString());
            Assert.AreEqual("archive_limit", (await Assert.ThrowsExactlyAsync<AdoException>(() => ArtifactArchiveInspector.InspectAsync(path, null, null, 100, CancellationToken.None))).Code);
        }
        finally { File.Delete(path); }
    }

    [TestMethod]
    public async Task ArchiveByteCeilingIsCheckedBeforeParsing()
    {
        string path = Path.GetTempFileName();
        try
        {
            using (var stream = File.OpenWrite(path)) stream.SetLength(64 * 1024 * 1024 + 1);
            Assert.AreEqual("archive_limit", (await Assert.ThrowsExactlyAsync<AdoException>(() => ArtifactArchiveInspector.InspectAsync(path, null, null, 100, CancellationToken.None))).Code);
        }
        finally { File.Delete(path); }
    }

    [TestMethod]
    public async Task InvalidZipAndCancellationAreHandled()
    {
        string path = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(path, "secret-invalid-zip");
            Assert.AreEqual("invalid_archive", (await Assert.ThrowsExactlyAsync<AdoException>(() => ArtifactArchiveInspector.InspectAsync(path, null, null, 100, CancellationToken.None))).Code);
            await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => ArtifactArchiveInspector.InspectAsync(path, null, null, 100, new CancellationToken(true)));
        }
        finally { File.Delete(path); }
    }
}
