using System.Text.Json;
using Ado.Application;
using Ado.Cli;
using Ado.Domain;
using Ado.Infrastructure;
using Ado.Infrastructure.Http;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Ado.Tests;

[TestClass]
public sealed class ReleaseEnvironmentTests
{
    private const string EnvironmentJson = """{"id":80,"releaseId":1492,"definitionEnvironmentId":7,"name":"synthetic\u001b stage","status":"succeeded","rank":1,"variables":{"value":"secret-sentinel"},"owner":{"displayName":"secret-sentinel"},"preDeployApprovals":[{"comments":"secret-sentinel"}],"deploySteps":[{"logUrl":"secret-sentinel"}]}""";
    private static string Body(string environments) => """{"id":1492,"name":"Release-58","releaseDefinition":{"id":20},"projectReference":{"id":"11111111-1111-1111-1111-111111111111","name":null},"environments":REPLACE}""".Replace("REPLACE", environments, StringComparison.Ordinal);

    [TestMethod]
    [DataRow("empty", 0, "complete")]
    [DataRow("single", 1, "complete")]
    [DataRow("limited", 1, "partial")]
    public async Task ReadsBoundedSafeEnvironmentMetadata(string mode, int count, string completeness)
    {
        using var handler = new TransportTests.FakeHandler(request =>
        {
            Assert.AreEqual("https://vsrm.dev.azure.com/example/Project/_apis/release/releases/1492?api-version=7.1", request.RequestUri!.AbsoluteUri);
            Assert.IsNotNull(request.Headers.Authorization);
            return TransportTests.Json(Body(mode == "empty" ? "[]" : "[" + EnvironmentJson + (mode == "limited" ? "," + EnvironmentJson.Replace("\"id\":80", "\"id\":81", StringComparison.Ordinal) : "") + "]"));
        });
        using var http = new HttpClient(handler);
        using var auth = new TokenAuthentication("pat", new("synthetic"));
        var result = await new ReleasesClient(new ServiceTransport(http, auth, "example"), "example", "Project").EnvironmentsAsync(1492, 1, CancellationToken.None);
        Assert.AreEqual(count, result.Items.Count);
        Assert.AreEqual(completeness, result.Meta.Completeness);
        Assert.IsNull(result.Meta.ContinuationToken);
        if (count != 0)
        {
            Assert.AreEqual("[REDACTED]\u001b stage", result.Items[0].Name);
            Assert.AreEqual("succeeded", result.Items[0].Status);
            Assert.AreEqual(7, result.Items[0].DefinitionEnvironmentId);
        }
        Assert.IsFalse(JsonSerializer.Serialize(result).Contains("secret-sentinel", StringComparison.Ordinal));
    }

    [TestMethod]
    [DataRow("missing")]
    [DataRow("null")]
    [DataRow("duplicate")]
    [DataRow("wrongRelease")]
    [DataRow("wrongEnvironmentRelease")]
    [DataRow("malformedId")]
    [DataRow("continuation")]
    public async Task RejectsInvalidResponses(string mode)
    {
        string body = Body("[" + EnvironmentJson + "]");
        body = mode switch
        {
            "missing" => body.Replace("\"environments\"", "\"other\"", StringComparison.Ordinal),
            "null" => Body("null"),
            "duplicate" => Body("[" + EnvironmentJson + "," + EnvironmentJson + "]"),
            "wrongRelease" => body.Replace("\"id\":1492", "\"id\":1491", StringComparison.Ordinal),
            "wrongEnvironmentRelease" => body.Replace("\"releaseId\":1492", "\"releaseId\":1491", StringComparison.Ordinal),
            "malformedId" => body.Replace("\"id\":80", "\"id\":0", StringComparison.Ordinal),
            _ => body
        };
        using var handler = new TransportTests.FakeHandler(_ =>
        {
            var response = TransportTests.Json(body);
            if (mode == "continuation") response.Headers.Add("x-ms-continuationtoken", "42");
            return response;
        });
        using var http = new HttpClient(handler);
        using var auth = new TokenAuthentication("pat", new("synthetic"));
        await Assert.ThrowsExactlyAsync<AdoException>(() => new ReleasesClient(new ServiceTransport(http, auth, "example"), "example", "Project")
            .EnvironmentsAsync(1492, 100, CancellationToken.None));
    }

    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public async Task CliStrictCompletenessRetainsSafeOutput(bool json)
    {
        using var handler = new TransportTests.FakeHandler(_ => TransportTests.Json(Body("[" + EnvironmentJson + "," + EnvironmentJson + "]")));
        using var output = new StringWriter();
        using var error = new StringWriter();
        int exit = await CliApp.RunAsync(["release", "environments", "--release-id", "1492", "--organization", "example", "--project", "Project",
            "--limit", "1", "--require-complete", "--read-only", "--non-interactive", "--output", json ? "json" : "table"], output, error,
            environment: key => key == "ADO_TOKEN" ? "synthetic" : null, testHandler: handler);
        Assert.AreEqual(10, exit, output.ToString());
        Assert.IsFalse(output.ToString().Contains("secret-sentinel", StringComparison.Ordinal));
        Assert.IsFalse(output.ToString().Contains('\u001b'));
        if (json)
        {
            using var doc = JsonDocument.Parse(output.ToString());
            Assert.AreEqual(1, doc.RootElement.GetProperty("data").GetArrayLength());
        }
        else Assert.IsTrue(output.ToString().Contains("ENVIRONMENT ID", StringComparison.Ordinal));
    }
}
