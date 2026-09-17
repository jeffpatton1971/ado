using Ado.Application;
using Ado.Domain;
using Ado.Infrastructure.Configuration;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Ado.Tests;

[TestClass]
public sealed class ConfigurationTests
{
    [TestMethod]
    public void SettingsUseFlagsThenEnvironmentThenSelectedProfile()
    {
        var file = new ConfigurationFile
        {
            DefaultProfile = "work",
            Profiles = new() { ["work"] = new() { Organization = "profile-org", Project = "profile-project" } }
        };
        string? Env(string key) => key == "ADO_ORGANIZATION" ? "env-org" : null;
        var result = ConfigurationResolver.Resolve(file, new(Project: "flag-project"), Env);
        Assert.AreEqual("env-org", result.Settings.Organization);
        Assert.AreEqual("flag-project", result.Settings.Project);
        Assert.AreEqual("environment", result.Sources["organization"]);
        Assert.AreEqual("argument", result.Sources["project"]);
    }

    [TestMethod]
    public void SelectedProfileDoesNotInheritDefaultProfileCredentials()
    {
        var file = new ConfigurationFile
        {
            DefaultProfile = "work",
            Profiles = new()
            {
                ["work"] = new() { Organization = "work", Authentication = new() { Provider = "macos-keychain", Service = "work", Account = "pat" } },
                ["personal"] = new() { Organization = "personal" }
            }
        };
        var result = ConfigurationResolver.Resolve(file, new(Profile: "personal"), _ => null);
        Assert.AreEqual("personal", result.Settings.Organization);
        Assert.AreEqual("environment", result.Settings.Authentication.Provider);
        Assert.IsNull(result.Settings.Authentication.Service);
    }

    [TestMethod]
    public void NativeReferenceCannotFollowOrganizationOverride()
    {
        var file = new ConfigurationFile
        {
            DefaultProfile = "work",
            Profiles = new() { ["work"] = new() { Organization = "work", Authentication = new() { Provider = "macos-keychain", Service = "work", Account = "pat" } } }
        };
        Assert.ThrowsExactly<AdoException>(() => ConfigurationResolver.Resolve(file, new(Organization: "other"), _ => null));
    }

    [TestMethod]
    public void PathsUseOperatingSystemConventions()
    {
        string home = Path.GetFullPath("home");
        string roaming = Path.GetFullPath("roaming");
        string xdg = Path.GetFullPath("xdg");
        Assert.AreEqual(Path.Combine(roaming, "ado", "config.json"), ConfigurationPaths.DefaultPath(PlatformFamily.Windows, home, roaming, null));
        Assert.AreEqual(Path.Combine(home, "Library", "Application Support", "ado", "config.json"), ConfigurationPaths.DefaultPath(PlatformFamily.MacOS, home, null, null));
        Assert.AreEqual(Path.Combine(xdg, "ado", "config.json"), ConfigurationPaths.DefaultPath(PlatformFamily.Linux, home, null, xdg));
        Assert.AreEqual(Path.Combine(home, ".config", "ado", "config.json"), ConfigurationPaths.DefaultPath(PlatformFamily.Linux, home, null, "relative-xdg"));
    }

    [TestMethod]
    [DataRow("{\"schemaVersion\":1,\"schemaVersion\":2}")]
    [DataRow("{\"profiles\":{\"work\":{\"authentication\":{\"token\":\"secret-sentinel\"}}}}")]
    [DataRow("{\"profiles\":null}")]
    [DataRow("{\"profiles\":{\"work\":{\"pagination\":null}}}")]
    [DataRow("{/* comment */}")]
    [DataRow("{\"schemaVersion\":1,}")]
    public async Task StrictSchemaRejectsUnsafeOrAmbiguousConfiguration(string json)
    {
        string path = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(path, json);
            var ex = await Assert.ThrowsExactlyAsync<AdoException>(() => ConfigurationLoader.LoadAsync(new(path, path, "argument"), CancellationToken.None));
            Assert.AreEqual(ExitCode.Configuration, ex.ExitCode);
            Assert.IsFalse(ex.Message.Contains("secret-sentinel", StringComparison.Ordinal));
        }
        finally { File.Delete(path); }
    }
}
