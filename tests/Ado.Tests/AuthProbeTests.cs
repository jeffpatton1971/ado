using System.Net;
using System.Text.Json;
using Ado.Cli;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Ado.Tests;

[TestClass]
public sealed class AuthProbeTests
{
    [TestMethod]
    [DataRow("build")]
    [DataRow("artifact")]
    [DataRow("feed")]
    public async Task ProbeMakesOneScopedGetAndReportsOnlyEndpointAccess(string capability)
    {
        int calls = 0;
        using var handler = new TransportTests.FakeHandler(request =>
        {
            calls++;
            Assert.AreEqual(HttpMethod.Get, request.Method);
            Assert.AreEqual(capability == "feed" ? "feeds.dev.azure.com" : "dev.azure.com", request.RequestUri!.Host);
            Assert.AreEqual(capability == "feed" ? "/example/_apis/packaging/feeds/automation" : "/example/Backend/_apis/build/builds/42" + (capability == "artifact" ? "/artifacts" : ""), request.RequestUri.AbsolutePath);
            return TransportTests.Json(capability == "feed" ? """{"id":"09093787-fb1b-4624-be02-8b4a92580114","name":"automation"}"""
                : capability == "artifact" ? """{"value":[]}""" : """{"id":42,"definition":{"id":1},"buildNumber":"test","status":"completed","result":"succeeded"}""");
        });
        var result = await Run(capability == "feed" ? ["--capability", capability, "--scope", "organization", "--feed", "automation"] : ["--capability", capability, "--build-id", "42"], handler);
        Assert.AreEqual(0, result.Exit, result.Output);
        Assert.AreEqual(1, calls);
        using var json = JsonDocument.Parse(result.Output);
        var data = json.RootElement.GetProperty("data");
        Assert.IsTrue(data.GetProperty("accessConfirmed").GetBoolean());
        Assert.AreEqual("not_independently_verified", data.GetProperty("credentialValidity").GetString());
        Assert.AreEqual(capability == "artifact" ? "build artifact list" : capability + " get", data.GetProperty("checkedCapability").GetString());
    }

    [TestMethod]
    [DataRow(401, 4)]
    [DataRow(403, 5)]
    [DataRow(404, 6)]
    public async Task EndpointFailuresAreNotReportedAsSuccessfulAuthentication(int status, int expected)
    {
        using var handler = new TransportTests.FakeHandler(_ => new HttpResponseMessage((HttpStatusCode)status));
        var result = await Run(["--capability", "artifact", "--build-id", "42"], handler);
        Assert.AreEqual(expected, result.Exit, result.Output);
        Assert.IsFalse(result.Output.Contains("accessConfirmed"));
    }

    [TestMethod]
    [DataRow("build")]
    [DataRow("artifact")]
    [DataRow("feed")]
    [DataRow("invalid")]
    public async Task InvalidContextFailsBeforeCredentialLookup(string capability)
    {
        using var handler = new TransportTests.FakeHandler(_ => throw new AssertFailedException("Unexpected network request"));
        var result = await Run(["--capability", capability], handler, false);
        Assert.AreEqual(2, result.Exit, result.Output);
    }

    private static async Task<(int Exit, string Output)> Run(string[] args, HttpMessageHandler handler, bool token = true)
    {
        string path = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(path, """{"defaultProfile":"test","profiles":{"test":{"organization":"example","project":"Backend"}}}""");
            using var output = new StringWriter(); using var error = new StringWriter();
            int exit = await CliApp.RunAsync(["auth", "check", .. args, "--config", path, "--json", "--read-only", "--non-interactive"], output, error,
                environment: key => key == "ADO_TOKEN" && token ? "synthetic" : null, testHandler: handler);
            return (exit, output.ToString());
        }
        finally { File.Delete(path); }
    }
}
