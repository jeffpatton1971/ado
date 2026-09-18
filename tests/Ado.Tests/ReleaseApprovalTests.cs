using System.Text.Json;
using Ado.Application;
using Ado.Cli;
using Ado.Domain;
using Ado.Infrastructure;
using Ado.Infrastructure.Http;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Ado.Tests;

[TestClass]
public sealed class ReleaseApprovalTests
{
    private const string Approval = """{"id":30,"status":"rejected","isAutomated":false,"attempt":1,"rank":1,"release":{"id":1492},"releaseEnvironment":{"id":1499},"comments":"secret-sentinel","approver":{"displayName":"secret-sentinel"},"history":[{"comments":"secret-sentinel"}],"url":"secret-sentinel"}""";
    private static string Body(string pre, string post) => """{"id":1492,"releaseDefinition":{"id":20},"environments":[{"id":1499,"releaseId":1492,"name":"synthetic\u001b env","preDeployApprovals":PRE,"postDeployApprovals":POST}]}"""
        .Replace("PRE", pre, StringComparison.Ordinal).Replace("POST", post, StringComparison.Ordinal);

    [TestMethod]
    [DataRow("empty", 10, "complete", 0)]
    [DataRow("both", 10, "complete", 2)]
    [DataRow("both", 1, "partial", 1)]
    [DataRow("missing", 10, "unknown", 1)]
    public async Task ReadsBoundedApprovalsWithoutPrivatePayloads(string mode, int limit, string completeness, int count)
    {
        string body = Body(mode == "empty" ? "[]" : "[" + Approval + "]",
            mode == "both" ? "[" + Approval.Replace("\"id\":30", "\"id\":31", StringComparison.Ordinal) + "]" : mode == "missing" ? "null" : "[]");
        using var handler = new TransportTests.FakeHandler(request =>
        {
            Assert.AreEqual("https://vsrm.dev.azure.com/example/Project/_apis/release/releases/1492?api-version=7.1", request.RequestUri!.AbsoluteUri);
            Assert.IsNotNull(request.Headers.Authorization);
            return TransportTests.Json(body);
        });
        using var http = new HttpClient(handler);
        using var auth = new TokenAuthentication("pat", new("synthetic"));
        var result = await new ReleasesClient(new ServiceTransport(http, auth, "example"), "example", "Project").ApprovalsAsync(1492, limit, CancellationToken.None);
        Assert.AreEqual(count, result.Items.Count);
        Assert.AreEqual(completeness, result.Meta.Completeness);
        Assert.IsNull(result.Meta.ContinuationToken);
        if (count > 0)
        {
            Assert.AreEqual("[REDACTED]\u001b env", result.Items[0].EnvironmentName);
            Assert.AreEqual("preDeploy", result.Items[0].Phase);
            Assert.AreEqual(false, result.Items[0].IsAutomated);
        }
        if (count == 2) Assert.AreEqual("postDeploy", result.Items[1].Phase);
        Assert.IsFalse(JsonSerializer.Serialize(result).Contains("secret-sentinel", StringComparison.Ordinal));
    }

    [TestMethod]
    [DataRow("duplicate")]
    [DataRow("wrongRelease")]
    [DataRow("wrongEnvironment")]
    [DataRow("badFlag")]
    [DataRow("badArray")]
    [DataRow("missingEnvironments")]
    [DataRow("continuation")]
    public async Task RejectsMalformedOrMismatchedApprovals(string mode)
    {
        string approval = mode switch
        {
            "wrongRelease" => Approval.Replace("\"id\":1492", "\"id\":1491", StringComparison.Ordinal),
            "wrongEnvironment" => Approval.Replace("\"id\":1499", "\"id\":1500", StringComparison.Ordinal),
            "badFlag" => Approval.Replace("false", "\"secret-sentinel\"", StringComparison.Ordinal),
            _ => Approval
        };
        string body = Body(mode == "badArray" ? "{}" : "[" + approval + "]", mode == "duplicate" ? "[" + approval + "]" : "[]");
        if (mode == "missingEnvironments") body = body.Replace("\"environments\"", "\"other\"", StringComparison.Ordinal);
        using var handler = new TransportTests.FakeHandler(_ =>
        {
            var response = TransportTests.Json(body);
            if (mode == "continuation") response.Headers.Add("x-ms-continuationtoken", "42");
            return response;
        });
        using var http = new HttpClient(handler);
        using var auth = new TokenAuthentication("pat", new("synthetic"));
        var error = await Assert.ThrowsExactlyAsync<AdoException>(() => new ReleasesClient(new ServiceTransport(http, auth, "example"), "example", "Project")
            .ApprovalsAsync(1492, 100, CancellationToken.None));
        Assert.IsFalse(error.Message.Contains("secret-sentinel", StringComparison.Ordinal));
    }

    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public async Task CliSupportsReadOnlyAndStrictCompleteness(bool json)
    {
        using var handler = new TransportTests.FakeHandler(_ => TransportTests.Json(Body("[" + Approval + "]", "null")));
        using var output = new StringWriter();
        using var error = new StringWriter();
        int exit = await CliApp.RunAsync(["release", "approvals", "--release-id", "1492", "--organization", "example", "--project", "Project",
            "--require-complete", "--read-only", "--non-interactive", "--output", json ? "json" : "table"], output, error,
            environment: key => key == "ADO_TOKEN" ? "synthetic" : null, testHandler: handler);
        Assert.AreEqual(10, exit, output.ToString());
        Assert.IsFalse(output.ToString().Contains("secret-sentinel", StringComparison.Ordinal));
        Assert.IsFalse(output.ToString().Contains('\u001b'));
        if (json)
        {
            using var document = JsonDocument.Parse(output.ToString());
            Assert.AreEqual(1, document.RootElement.GetProperty("data").GetArrayLength());
        }
        else Assert.IsTrue(output.ToString().Contains("APPROVAL ID", StringComparison.Ordinal));
    }
}
