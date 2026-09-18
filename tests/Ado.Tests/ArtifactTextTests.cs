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
public sealed class ArtifactTextTests
{
    private static string Zip(byte[] bytes)
    {
        string path = Path.Combine(Path.GetTempPath(), "ado-text-" + Guid.NewGuid().ToString("N") + ".zip");
        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
        using var entry = archive.CreateEntry("plugin.json").Open();
        entry.Write(bytes);
        return path;
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task TextIsOptInBoundedAndKeepsFullMemberHash(bool json)
    {
        byte[] bytes = Encoding.UTF8.GetBytes("\uFEFFfirst\u001b[31m\r\nsecond\nthird");
        string path = Zip(bytes);
        try
        {
            var metadata = await ArtifactArchiveInspector.InspectAsync(path, "plugin.json", null, 100, CancellationToken.None);
            Assert.IsNull(metadata.Data.Entries.Single().TextLines);
            using var output = new StringWriter(); using var error = new StringWriter();
            int exit = await CliApp.RunAsync(["artifact", "inspect", "--file", path, "--entry", "plugin.json", "--show-text", "--text-lines", "2",
                "--require-complete", "--non-interactive", "--read-only", "--output", json ? "json" : "table"], output, error, environment: _ => null);
            Assert.AreEqual(10, exit, output.ToString());
            Assert.IsFalse(output.ToString().Contains('\u001b'));
            StringAssert.Contains(output.ToString(), Convert.ToHexStringLower(SHA256.HashData(bytes)));
            Assert.IsFalse(output.ToString().Contains("third", StringComparison.Ordinal));
            if (json)
            {
                using var document = JsonDocument.Parse(output.ToString());
                var entry = document.RootElement.GetProperty("data").GetProperty("entries")[0];
                Assert.AreEqual(3, entry.GetProperty("textLineCount").GetInt32());
                Assert.AreEqual(2, entry.GetProperty("textLines").GetArrayLength());
                Assert.AreEqual("first\u001b[31m", entry.GetProperty("textLines")[0].GetString());
                Assert.AreEqual("line_limit", document.RootElement.GetProperty("meta").GetProperty("truncationReason").GetString());
            }
        }
        finally { File.Delete(path); }
    }

    [TestMethod]
    [DataRow("invalidUtf8", "invalid_archive_text")]
    [DataRow("nul", "invalid_archive_text")]
    [DataRow("oversize", "archive_text_limit")]
    public async Task UnsupportedContentFailsBeforeOutput(string mode, string code)
    {
        byte[] content = mode == "invalidUtf8" ? [0xff, 0xfe] : mode == "nul" ? [65, 0, 66] : new byte[1024 * 1024 + 1];
        string path = Zip(content);
        try
        {
            var error = await Assert.ThrowsExactlyAsync<AdoException>(() => ArtifactArchiveInspector.InspectAsync(path, "plugin.json", null, 100, CancellationToken.None, true));
            Assert.AreEqual(code, error.Code);
        }
        finally { File.Delete(path); }
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("one line")]
    public async Task CompleteTextAndEmptyTextHaveCorrectCounts(string content)
    {
        string path = Zip(Encoding.UTF8.GetBytes(content));
        try
        {
            var result = await ArtifactArchiveInspector.InspectAsync(path, "plugin.json", null, 100, CancellationToken.None, true);
            Assert.AreEqual("complete", result.Meta.Completeness);
            Assert.AreEqual(content.Length == 0 ? 0 : 1, result.Data.Entries[0].TextLineCount);
        }
        finally { File.Delete(path); }
    }

    [TestMethod]
    public async Task MissingEntryAndOrphanedLineOptionAreUsageErrors()
    {
        using var output = new StringWriter(); using var error = new StringWriter();
        Assert.AreEqual(2, await CliApp.RunAsync(["artifact", "inspect", "--file", "nonexistent.zip", "--show-text", "--json"], output, error, environment: _ => null));
        Assert.AreEqual(2, await CliApp.RunAsync(["artifact", "inspect", "--file", "nonexistent.zip", "--text-lines", "2", "--json"], output, error, environment: _ => null));
    }
}
