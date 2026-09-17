using System.Text.Json;
using Ado.Cli;
using Microsoft.VisualStudio.TestTools.UnitTesting;

[assembly: Parallelize(Scope = ExecutionScope.MethodLevel)]

namespace Ado.Tests;

[TestClass]
public sealed class CliTests
{
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
}
