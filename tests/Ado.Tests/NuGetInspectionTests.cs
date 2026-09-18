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
public sealed class NuGetInspectionTests
{
    [TestMethod]
    public async Task SelectedManifestAndBinaryAssemblyAreInspectedAsBytesWithoutRemoteAccess()
    {
        string path = Zip("<package><metadata><id>Example</id><version>1.0.0</version></metadata></package>");
        var members = new Dictionary<string, byte[]>
        {
            ["plugin.json"] = Encoding.UTF8.GetBytes("{\"entryAssembly\":\"Example.dll\"}"),
            // Intentionally invalid UTF-8 and not a loadable assembly: inspection must treat it as bytes.
            ["lib/net10.0/Example.dll"] = [0x4d, 0x5a, 0, 0xff, 0xfe, 0x80, 0x01]
        };
        try
        {
            using (var archive = ZipFile.Open(path, ZipArchiveMode.Update))
                foreach (var member in members)
                {
                    using var stream = archive.CreateEntry(member.Key).Open();
                    stream.Write(member.Value);
                }
            string digest = Convert.ToHexStringLower(SHA256.HashData(await File.ReadAllBytesAsync(path)));
            using var handler = new TransportTests.FakeHandler(_ => throw new AssertFailedException("Local inspection made an HTTP request"));
            foreach (var member in members)
            {
                using var output = new StringWriter(); using var error = new StringWriter();
                int exit = await CliApp.RunAsync(["artifact", "inspect", "--file", path, "--entry", member.Key,
                    "--expected-sha256", digest, "--config", path + ".missing", "--json", "--non-interactive", "--read-only"],
                    output, error, environment: _ => null, testHandler: handler);
                Assert.AreEqual(0, exit, error.ToString());
                using var json = JsonDocument.Parse(output.ToString());
                var data = json.RootElement.GetProperty("data");
                Assert.AreEqual(digest, data.GetProperty("sha256").GetString());
                var entries = data.GetProperty("entries");
                Assert.AreEqual(1, entries.GetArrayLength());
                Assert.AreEqual(member.Key, entries[0].GetProperty("path").GetString());
                Assert.AreEqual(member.Value.Length, entries[0].GetProperty("bytes").GetInt32());
                Assert.AreEqual(Convert.ToHexStringLower(SHA256.HashData(member.Value)), entries[0].GetProperty("sha256").GetString());
                Assert.AreEqual(JsonValueKind.Null, entries[0].GetProperty("textLines").ValueKind);
                Assert.AreEqual("complete", json.RootElement.GetProperty("meta").GetProperty("completeness").GetString());
            }
            Assert.AreEqual(0, handler.Calls);
        }
        finally { File.Delete(path); }
    }

    private static string Zip(string xml, bool duplicate = false)
    {
        string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".nupkg");
        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
        using (var writer = new StreamWriter(archive.CreateEntry("Example.nuspec").Open(), new UTF8Encoding(false))) writer.Write(xml);
        if (duplicate) archive.CreateEntry("Other.nuspec");
        return path;
    }

    [TestMethod]
    public async Task PreservesDeclarationsAndEmptyGroupsWithoutConfigAccess()
    {
        string path = Zip("""
            <package xmlns="http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd"><metadata><id>Example</id><version>1.0.0</version>
            <dependencies><group targetFramework="net9.0"><dependency id="Other" version="[1.0,2.0)" exclude="Build,Analyzers" /></group><group targetFramework="net10.0" /></dependencies></metadata></package>
            """);
        try
        {
            var result = await NuGetPackageInspector.InspectAsync(path, null, CancellationToken.None);
            Assert.AreEqual("[1.0,2.0)", result.DependencyGroups[0].Dependencies[0].Version);
            Assert.AreEqual(0, result.DependencyGroups[1].Dependencies.Count);
            Assert.AreEqual(64, result.ManifestSha256.Length);
            using var output = new StringWriter(); using var error = new StringWriter();
            int exit = await CliApp.RunAsync(["package", "inspect", "--file", path, "--config", "missing.json", "--read-only", "--json"], output, error, environment: _ => null);
            Assert.AreEqual(0, exit, error.ToString());
            StringAssert.Contains(output.ToString(), result.ArchiveSha256);
        }
        finally { File.Delete(path); }
    }

    [TestMethod]
    [DataRow("<!DOCTYPE package [<!ENTITY x SYSTEM 'file:///no-read'>]><package><metadata><id>&x;</id><version>1</version></metadata></package>", false)]
    [DataRow("<package><metadata><id>A</id><id>B</id><version>1</version></metadata></package>", false)]
    [DataRow("<package><metadata><id>A</id><version>1</version></metadata></package>", true)]
    [DataRow("<package><metadata><id>A</id><version>1</version><dependencies><group/><dependency id='B'/></dependencies></metadata></package>", false)]
    public async Task RejectsAmbiguousOrUnsafeManifest(string xml, bool duplicate)
    {
        string path = Zip(xml, duplicate);
        try
        {
            var error = await Assert.ThrowsExactlyAsync<AdoException>(() => NuGetPackageInspector.InspectAsync(path, null, CancellationToken.None));
            Assert.AreEqual("invalid_package_manifest", error.Code);
        }
        finally { File.Delete(path); }
    }

    [TestMethod]
    public async Task DoesNotParseTruncatedText()
    {
        string path = Zip("<package>" + new string('\n', 10001) + "</package>");
        try
        {
            var error = await Assert.ThrowsExactlyAsync<AdoException>(() => NuGetPackageInspector.InspectAsync(path, null, CancellationToken.None));
            Assert.AreEqual("package_manifest_limit", error.Code);
        }
        finally { File.Delete(path); }
    }
}
