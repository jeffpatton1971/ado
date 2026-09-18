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
public sealed class ArtifactExtractionTests
{
    private static string Zip(string directory, params string[] entries)
    {
        string path = Path.Combine(directory, "input.zip");
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        foreach (string name in entries)
        {
            using var output = zip.CreateEntry(name).Open();
            output.Write(Encoding.UTF8.GetBytes("manifest payload"));
        }
        return path;
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task CliExtractsOnlySelectedMemberOrPreviewsWithoutWriting(bool dryRun)
    {
        string directory = Directory.CreateTempSubdirectory("ado-extract-").FullName;
        try
        {
            string zip = Zip(directory, "nested/plugin.json", "unselected.txt");
            string target = Path.Combine(directory, "selected.json");
            using var handler = new TransportTests.FakeHandler(_ => throw new AssertFailedException("Network access"));
            using var output = new StringWriter(); using var error = new StringWriter();
            string[] extra = dryRun ? ["--dry-run"] : [];
            int exit = await CliApp.RunAsync(["artifact", "extract", "--file", zip, "--entry", "nested/plugin.json", "--destination", target,
                "--json", "--non-interactive", "--read-only", .. extra], output, error,
                environment: key => key == "ADO_CONFIG" ? "does-not-exist.json" : null, testHandler: handler);
            Assert.AreEqual(0, exit, output.ToString());
            Assert.AreEqual(0, handler.Calls);
            using var json = JsonDocument.Parse(output.ToString());
            var result = json.RootElement.GetProperty("data");
            Assert.AreEqual(!dryRun, result.GetProperty("written").GetBoolean());
            Assert.AreEqual(!dryRun, File.Exists(target));
            Assert.AreEqual(dryRun ? 1 : 2, Directory.GetFiles(directory).Length);
            Assert.AreEqual(0, Directory.GetDirectories(directory).Length);
            if (!dryRun)
            {
                byte[] bytes = await File.ReadAllBytesAsync(target);
                if (!OperatingSystem.IsWindows()) Assert.AreEqual(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(target));
                Assert.AreEqual("manifest payload", Encoding.UTF8.GetString(bytes));
                Assert.AreEqual(Convert.ToHexStringLower(SHA256.HashData(bytes)), result.GetProperty("memberSha256").GetString());
            }
        }
        finally { Directory.Delete(directory, true); }
    }

    [TestMethod]
    [DataRow("exists", "destination_exists")]
    [DataRow("missing", "archive_entry_not_found")]
    [DataRow("directory", "archive_entry_required")]
    [DataRow("unsafe", "unsafe_archive")]
    [DataRow("hash", "archive_hash_mismatch")]
    public async Task ValidationFailureDoesNotPublishOrOverwrite(string mode, string code)
    {
        string directory = Directory.CreateTempSubdirectory("ado-extract-").FullName;
        try
        {
            string zip = Zip(directory, "plugin.json", mode == "unsafe" ? "../outside" : "other");
            string target = Path.Combine(directory, "selected.json");
            if (mode == "exists") await File.WriteAllTextAsync(target, "original");
            var error = await Assert.ThrowsExactlyAsync<AdoException>(() => ArtifactArchiveInspector.ExtractAsync(zip,
                mode == "directory" ? "folder/" : mode == "missing" ? "absent" : "plugin.json", target,
                mode == "hash" ? new string('0', 64) : null, false, CancellationToken.None));
            Assert.AreEqual(code, error.Code);
            Assert.AreEqual(mode == "exists", File.Exists(target));
            if (mode == "exists") Assert.AreEqual("original", await File.ReadAllTextAsync(target));
            Assert.AreEqual(0, Directory.GetFiles(directory, ".ado-*.partial").Length);
        }
        finally { Directory.Delete(directory, true); }
    }

    [TestMethod]
    public async Task UnsupportedCompressionCleansTemporaryFile()
    {
        string directory = Directory.CreateTempSubdirectory("ado-extract-").FullName;
        try
        {
            string zip = Zip(directory, "plugin.json");
            byte[] bytes = await File.ReadAllBytesAsync(zip);
            // Mark both local and central headers with an unsupported compression method.
            bytes[8] = 99;
            for (int i = 0; i < bytes.Length - 12; i++)
                if (bytes[i] == 0x50 && bytes[i + 1] == 0x4b && bytes[i + 2] == 1 && bytes[i + 3] == 2) { bytes[i + 10] = 99; break; }
            await File.WriteAllBytesAsync(zip, bytes);
            string target = Path.Combine(directory, "selected.json");
            await Assert.ThrowsExactlyAsync<AdoException>(() => ArtifactArchiveInspector.ExtractAsync(zip, "plugin.json", target, null, false, CancellationToken.None));
            Assert.IsFalse(File.Exists(target));
            Assert.AreEqual(1, Directory.GetFiles(directory).Length);
        }
        finally { Directory.Delete(directory, true); }
    }
}
