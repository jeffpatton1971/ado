using System.Text.Json;
using Ado.Application;
using Ado.Cli;
using Ado.Domain;
using Ado.Infrastructure;
using Ado.Infrastructure.Http;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Ado.Tests;

[TestClass]
public sealed class ReleaseDeploymentTests
{
    private const string Step = """{"id":30,"deploymentId":40,"attempt":1,"status":"failed","operationStatus":"phaseFailed","hasStarted":true,"issues":[{"message":"secret-sentinel"}],"requestedBy":{"displayName":"secret-sentinel"},"releaseDeployPhases":[{"errorLog":"secret-sentinel"}]}""";
    private static string Body(string steps) => """{"id":1492,"releaseDefinition":{"id":20},"environments":[{"id":1499,"releaseId":1492,"name":"synthetic\u001b env","deploySteps":STEPS}]}""".Replace("STEPS", steps, StringComparison.Ordinal);

    [TestMethod]
    [DataRow("empty", "complete", 0)]
    [DataRow("single", "complete", 1)]
    [DataRow("limited", "partial", 1)]
    [DataRow("missing", "unknown", 0)]
    public async Task ReadsBoundedSafeAttempts(string mode, string completeness, int count)
    {
        string steps = mode == "empty" ? "[]" : mode == "missing" ? "null" : "[" + Step +
            (mode == "limited" ? "," + Step.Replace("\"id\":30", "\"id\":31", StringComparison.Ordinal) : "") + "]";
        using var handler = new TransportTests.FakeHandler(request =>
        {
            Assert.AreEqual("https://vsrm.dev.azure.com/example/Project/_apis/release/releases/1492?api-version=7.1", request.RequestUri!.AbsoluteUri);
            Assert.IsNotNull(request.Headers.Authorization);
            return TransportTests.Json(Body(steps));
        });
        using var http = new HttpClient(handler);
        using var auth = new TokenAuthentication("pat", new("synthetic"));
        var result = await new ReleasesClient(new ServiceTransport(http, auth, "example"), "example", "Project").DeploymentsAsync(1492, 1, CancellationToken.None);
        Assert.AreEqual(count, result.Items.Count);
        Assert.AreEqual(completeness, result.Meta.Completeness);
        Assert.IsNull(result.Meta.ContinuationToken);
        if (count > 0)
        {
            Assert.AreEqual("[REDACTED]\u001b env", result.Items[0].EnvironmentName);
            Assert.AreEqual("phaseFailed", result.Items[0].OperationStatus);
            Assert.AreEqual(true, result.Items[0].HasStarted);
        }
        Assert.IsFalse(JsonSerializer.Serialize(result).Contains("secret-sentinel", StringComparison.Ordinal));
    }

    [TestMethod]
    [DataRow("duplicate")]
    [DataRow("wrongRelease")]
    [DataRow("wrongEnvironmentRelease")]
    [DataRow("badFlag")]
    [DataRow("badArray")]
    [DataRow("badAttempt")]
    [DataRow("continuation")]
    public async Task RejectsInvalidAttempts(string mode)
    {
        string body = Body(mode == "badArray" ? "{}" : "[" + Step + (mode == "duplicate" ? "," + Step : "") + "]");
        body = mode switch
        {
            "wrongRelease" => body.Replace("\"id\":1492", "\"id\":1491", StringComparison.Ordinal),
            "wrongEnvironmentRelease" => body.Replace("\"releaseId\":1492", "\"releaseId\":1491", StringComparison.Ordinal),
            "badFlag" => body.Replace("true", "\"secret-sentinel\"", StringComparison.Ordinal),
            "badAttempt" => body.Replace("\"attempt\":1", "\"attempt\":-1", StringComparison.Ordinal),
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
            .DeploymentsAsync(1492, 100, CancellationToken.None));
    }

    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public async Task CliPreservesPartialOutput(bool json)
    {
        using var handler = new TransportTests.FakeHandler(_ => TransportTests.Json(Body("[" + Step + "," + Step.Replace("\"id\":30", "\"id\":31", StringComparison.Ordinal) + "]")));
        using var output = new StringWriter();
        using var error = new StringWriter();
        int exit = await CliApp.RunAsync(["release", "deployments", "--release-id", "1492", "--organization", "example", "--project", "Project",
            "--limit", "1", "--require-complete", "--read-only", "--non-interactive", "--output", json ? "json" : "table"], output, error,
            environment: key => key == "ADO_TOKEN" ? "synthetic" : null, testHandler: handler);
        Assert.AreEqual(10, exit, output.ToString());
        Assert.IsFalse(output.ToString().Contains("secret-sentinel", StringComparison.Ordinal));
        Assert.IsFalse(output.ToString().Contains('\u001b'));
        if (json)
        {
            using var document = JsonDocument.Parse(output.ToString());
            Assert.AreEqual(1, document.RootElement.GetProperty("data").GetArrayLength());
        }
        else Assert.IsTrue(output.ToString().Contains("OPERATION STATUS", StringComparison.Ordinal));
    }
}
