using System.IO.Compression;
using System.Text;
using Ado.Cli;
using Ado.Domain;
using Ado.Infrastructure;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Ado.Tests;

[TestClass]
public sealed class NuGetInspectionTests
{
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
