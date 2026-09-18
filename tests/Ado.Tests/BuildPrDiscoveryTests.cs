using System.Text.Json;
using Ado.Cli;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Ado.Tests;

[TestClass]
public sealed class BuildPrDiscoveryTests
{
    private const string MergeSha = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private static string Build(string branch = "refs/pull/42/merge", string reason = "pullRequest", string? repositoryId = "owner/repo", string type = "GitHub") =>
        JsonSerializer.Serialize(new { id = 34, definition = new { id = 12 }, sourceBranch = branch, sourceVersion = MergeSha, reason, repository = new { id = repositoryId, type } });

    [TestMethod]
    [DataRow("GitHub", "owner/repo")]
    [DataRow("TfsGit", "b825db5d-9ab5-4484-a233-3cb86df28249")]
    public async Task RepositoryScopedMergeRefAndReasonSurviveContinuation(string type, string repository)
    {
        int calls = 0;
        using var handler = new TransportTests.FakeHandler(request =>
        {
            calls++;
            Assert.AreEqual(HttpMethod.Get, request.Method);
            string query = request.RequestUri!.Query;
            foreach (string expected in new[] { "branchName=refs%2Fpull%2F42%2Fmerge", "reasonFilter=pullRequest", "repositoryId=" + Uri.EscapeDataString(repository), "repositoryType=" + type, "definitions=12" })
                StringAssert.Contains(query, expected);
            Assert.IsFalse(query.Contains("prNumber", StringComparison.Ordinal));
            if (calls == 2) StringAssert.Contains(query, "continuationToken=next%2Bpage");
            var response = TransportTests.Json("{\"value\":[" + Build(repositoryId: repository, type: type) + "]}");
            if (calls == 1) response.Headers.Add("x-ms-continuationtoken", "next+page");
            return response;
        });
        string[] args = ["--pr-number", "42", "--repository-id", repository, "--repository-type", type, "--definition-id", "12", "--limit", "1", "--require-complete"];
        var first = await Run(args, handler);
        Assert.AreEqual(10, first.Exit, first.Output);
        using var json = JsonDocument.Parse(first.Output);
        Assert.AreEqual("next+page", json.RootElement.GetProperty("meta").GetProperty("continuationToken").GetString());
        Assert.AreEqual(MergeSha, json.RootElement.GetProperty("data")[0].GetProperty("sourceVersion").GetString());
        var context = json.RootElement.GetProperty("data")[0].GetProperty("pullRequestContext");
        Assert.AreEqual(42, context.GetProperty("number").GetInt32());
        Assert.AreEqual("reported_merge_commit", context.GetProperty("builtVersionKind").GetString());
        Assert.AreEqual(JsonValueKind.Null, context.GetProperty("headVersion").ValueKind);
        var second = await Run([.. args, "--continuation-token", "next+page"], handler);
        Assert.AreEqual(0, second.Exit, second.Output);
        Assert.AreEqual(2, calls);
    }

    [TestMethod]
    [DataRow("refs/heads/main", "pullRequest", "owner/repo", "GitHub")]
    [DataRow("refs/pull/43/merge", "pullRequest", "owner/repo", "GitHub")]
    [DataRow("refs/pull/42/merge", "manual", "owner/repo", "GitHub")]
    [DataRow("refs/pull/42/merge", "pullRequest", "other/repo", "GitHub")]
    [DataRow("refs/pull/42/merge", "pullRequest", null, "GitHub")]
    [DataRow("refs/pull/42/merge", "pullRequest", "owner/repo", "TfsGit")]
    public async Task WrongOrMissingIdentityCannotSatisfyPrQuery(string branch, string reason, string? repository, string type)
    {
        using var handler = new TransportTests.FakeHandler(_ => TransportTests.Json("{\"value\":[" + Build(branch, reason, repository, type) + "]}"));
        var result = await Run(["--pr-number", "42", "--repository-id", "owner/repo", "--repository-type", "GitHub"], handler);
        Assert.AreEqual(9, result.Exit, result.Output);
        StringAssert.Contains(result.Output, "invalid_service_response");
    }

    [TestMethod]
    public async Task PrHeadShaDoesNotMatchDifferentBuiltMergeSha()
    {
        using var handler = new TransportTests.FakeHandler(_ => TransportTests.Json("{\"value\":[" + Build() + "]}"));
        var result = await Run(["--pr-number", "42", "--repository-id", "owner/repo", "--repository-type", "GitHub", "--source-sha", new string('b', 40)], handler);
        Assert.AreEqual(0, result.Exit, result.Output);
        using var json = JsonDocument.Parse(result.Output);
        Assert.AreEqual(0, json.RootElement.GetProperty("data").GetArrayLength());
        Assert.AreEqual(1, json.RootElement.GetProperty("meta").GetProperty("scannedCount").GetInt32());
        Assert.AreEqual("complete", json.RootElement.GetProperty("meta").GetProperty("completeness").GetString());
    }

    [TestMethod]
    [DataRow("--pr-number", "0", "--repository-id", "owner/repo", "--repository-type", "GitHub")]
    [DataRow("--pr-number", "42")]
    [DataRow("--pr-number", "42", "--repository-id", "owner/repo")]
    [DataRow("--pr-number", "42", "--repository-id", "owner/repo", "--repository-type", "TFVC")]
    [DataRow("--pr-number", "42", "--repository-id", "owner/repo", "--repository-type", "GitHub", "--branch", "refs/heads/main")]
    public async Task InvalidPrScopeFailsBeforeAuthentication(params string[] args)
    {
        using var handler = new TransportTests.FakeHandler(_ => throw new AssertFailedException("Unexpected HTTP"));
        var result = await Run(args, handler, token: false);
        Assert.AreEqual(2, result.Exit, result.Output);
        Assert.AreEqual(0, handler.Calls);
    }

    private static async Task<(int Exit, string Output)> Run(string[] args, HttpMessageHandler handler, bool token = true)
    {
        string path = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(path, """{"defaultProfile":"test","profiles":{"test":{"organization":"example","project":"Backend"}}}""");
            using var output = new StringWriter();
            using var error = new StringWriter();
            int exit = await CliApp.RunAsync(["build", "list", .. args, "--config", path, "--json", "--read-only", "--non-interactive"], output, error,
                environment: key => key == "ADO_TOKEN" && token ? "synthetic" : null, testHandler: handler);
            return (exit, output.ToString());
        }
        finally { File.Delete(path); }
    }
}
