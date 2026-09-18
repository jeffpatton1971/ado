using System.Text.Json;
using Ado.Cli;
using Microsoft.VisualStudio.TestTools.UnitTesting;

[assembly: Parallelize(Scope = ExecutionScope.MethodLevel)]

namespace Ado.Tests;

[TestClass]
public sealed class CliTests
{
    [TestMethod]
    [DataRow("--help")]
    [DataRow("-h")]
    [DataRow("-?")]
    public async Task AllHelpAliasesWorkWithoutConfigurationOrCredentials(string alias)
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        int result = await CliApp.RunAsync(["project", "list", alias], output, error,
            environment: key => key == "ADO_OUTPUT" ? null : throw new AssertFailedException("Help must not resolve configuration or credentials."));
        Assert.AreEqual(0, result);
        StringAssert.Contains(output.ToString(), "Usage:");
    }

    [TestMethod]
    public async Task VersionIsCleanSemanticVersion()
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        Assert.AreEqual(0, await CliApp.RunAsync(["--version"], output, error));
        StringAssert.Matches(output.ToString().Trim(), new(@"^\d+\.\d+\.\d+$"));
        Assert.AreEqual("", error.ToString());
    }

    [TestMethod]
    public async Task InvalidArgumentsProduceJsonWithoutEchoingSecrets()
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        Assert.AreEqual(2, await CliApp.RunAsync(["--json", "--unknown", "secret-sentinel"], output, error));
        using var document = JsonDocument.Parse(output.ToString());
        Assert.IsFalse(document.RootElement.GetProperty("ok").GetBoolean());
        Assert.AreEqual("invalid_arguments", document.RootElement.GetProperty("error").GetProperty("code").GetString());
        Assert.IsFalse(output.ToString().Contains("secret-sentinel", StringComparison.Ordinal));
        Assert.AreEqual("", error.ToString());
    }

    [TestMethod]
    public async Task CancellationStillProducesJsonEnvelope()
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        Assert.AreEqual(130, await CliApp.RunAsync(["config", "paths", "--json"], output, error, new CancellationToken(true)));
        using var document = JsonDocument.Parse(output.ToString());
        Assert.AreEqual("cancelled", document.RootElement.GetProperty("error").GetProperty("code").GetString());
        Assert.AreEqual("", error.ToString());
    }

    [TestMethod]
    [DataRow("--limit", "not-a-number")]
    [DataRow("--output", "unknown")]
    [DataRow("--timeout", "999999999999999999")]
    public async Task MalformedOptionsRemainSafeJson(string option, string value)
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        Assert.AreEqual(2, await CliApp.RunAsync(["config", "show", "--json", option, value], output, error, environment: _ => null));
        using var document = JsonDocument.Parse(output.ToString());
        Assert.IsFalse(document.RootElement.GetProperty("ok").GetBoolean());
        Assert.AreEqual("", error.ToString());
    }

    [TestMethod]
    public async Task ConfigShowUsesProfileJsonPreferenceAndDoesNotReadTokenEnvironment()
    {
        string path = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(path, """{"defaultProfile":"work","profiles":{"work":{"organization":"example","output":"json"}}}""");
            using var output = new StringWriter();
            using var error = new StringWriter();
            string? Env(string key)
            {
                Assert.AreNotEqual("ADO_TOKEN", key);
                return null;
            }
            Assert.AreEqual(0, await CliApp.RunAsync(["config", "show", "--effective", "--config", path], output, error, environment: Env));
            using var document = JsonDocument.Parse(output.ToString());
            Assert.AreEqual("example", document.RootElement.GetProperty("data").GetProperty("settings").GetProperty("organization").GetString());
        }
        finally { File.Delete(path); }
    }
}
