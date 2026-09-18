using System.Xml;
using System.Xml.Linq;
using Ado.Domain;

namespace Ado.Infrastructure;

public sealed record NuGetDependencyDeclaration(string Id, string? Version, string? Include, string? Exclude);
public sealed record NuGetDependencyGroup(string? TargetFramework, IReadOnlyList<NuGetDependencyDeclaration> Dependencies);
public sealed record NuGetManifestInspection(string Id, string Version, long ArchiveBytes, string ArchiveSha256,
    string ManifestPath, string ManifestSha256, IReadOnlyList<NuGetDependencyGroup> DependencyGroups, IReadOnlyList<string> Limitations);

public static class NuGetPackageInspector
{
    public static async Task<NuGetManifestInspection> InspectAsync(string? path, string? expectedHash, CancellationToken cancellationToken)
    {
        var inspection = await ArtifactArchiveInspector.InspectManifestAsync(path, expectedHash, cancellationToken);
        if (inspection.Meta.Truncated)
            throw new AdoException("package_manifest_limit", "The manifest exceeds the 10000-line inspection limit.", ExitCode.Safety);
        var member = inspection.Data.Entries.Single();
        try
        {
            using var input = new StringReader(string.Join('\n', member.TextLines!));
            using var reader = XmlReader.Create(input, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 1024 * 1024 });
            var document = XDocument.Load(reader);
            var root = document.Root ?? throw Invalid();
            XNamespace ns = root.Name.Namespace;
            if (root.Name.LocalName != "package" || (ns != XNamespace.None && !KnownNamespaces.Contains(ns.NamespaceName))) throw Invalid();
            var metadata = One(root, ns + "metadata", true)!;
            string id = Value(One(metadata, ns + "id", true)!, true)!;
            string version = Value(One(metadata, ns + "version", true)!, true)!;
            var dependencies = One(metadata, ns + "dependencies", false);
            var groups = new List<NuGetDependencyGroup>();
            if (dependencies is not null)
            {
                var children = dependencies.Elements().ToArray();
                if (children.Any(e => e.Name != ns + "group" && e.Name != ns + "dependency")) throw Invalid();
                if (children.Any(e => e.Name == ns + "group"))
                {
                    if (children.Any(e => e.Name != ns + "group")) throw Invalid();
                    foreach (var group in children)
                        groups.Add(new(Text((string?)group.Attribute("targetFramework"), false), ReadDependencies(group, ns)));
                }
                else if (children.Length > 0) groups.Add(new(null, ReadDependencies(dependencies, ns)));
            }
            cancellationToken.ThrowIfCancellationRequested();
            return new(id, version, inspection.Data.ArchiveBytes, inspection.Data.Sha256, member.Path, member.Sha256!, groups,
                ["Local manifest declarations only; no feed or origin verification was performed.",
                 "Dependency version strings are declarations, not resolved versions. Framework compatibility and dependency closure were not evaluated.",
                 "Hashes identify inspected bytes; package signatures, publisher authenticity and assemblies were not verified."]);
        }
        catch (XmlException) { throw Invalid(); }
    }

    private static readonly HashSet<string> KnownNamespaces = [
        "http://schemas.microsoft.com/packaging/2010/07/nuspec.xsd", "http://schemas.microsoft.com/packaging/2011/08/nuspec.xsd",
        "http://schemas.microsoft.com/packaging/2011/10/nuspec.xsd", "http://schemas.microsoft.com/packaging/2012/06/nuspec.xsd",
        "http://schemas.microsoft.com/packaging/2013/01/nuspec.xsd", "http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd"];

    private static IReadOnlyList<NuGetDependencyDeclaration> ReadDependencies(XElement parent, XNamespace ns) => parent.Elements().Select(e =>
    {
        if (e.Name != ns + "dependency" || e.HasElements) throw Invalid();
        return new NuGetDependencyDeclaration(Text((string?)e.Attribute("id"), true)!, Text((string?)e.Attribute("version"), false),
            Text((string?)e.Attribute("include"), false), Text((string?)e.Attribute("exclude"), false));
    }).ToArray();

    private static XElement? One(XElement parent, XName name, bool required)
    {
        var elements = parent.Elements(name).ToArray();
        if (elements.Length > 1 || (required && elements.Length != 1)) throw Invalid();
        return elements.SingleOrDefault();
    }
    private static string? Value(XElement element, bool required) => element.HasElements ? throw Invalid() : Text(element.Value, required);
    private static string? Text(string? value, bool required)
    {
        if ((required && string.IsNullOrWhiteSpace(value)) || value?.Length > 2048) throw Invalid();
        return value;
    }
    private static AdoException Invalid() => new("invalid_package_manifest", "The nuspec is malformed, ambiguous or uses an unsupported manifest structure.", ExitCode.Safety);
}
