using System.Text.Json;
using Ado.Application;
using Ado.Domain;
using Ado.Infrastructure;
using Ado.Infrastructure.Http;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Ado.Tests;

[TestClass]
public sealed class RunRepositoryProvenanceTests
{
    [TestMethod]
    public async Task RunProjectsReportedVersionsWithoutReadingCurrentBranchesOrLeakingPayloads()
    {
        using var handler = new TransportTests.FakeHandler(request =>
        {
            Assert.AreEqual(HttpMethod.Get, request.Method);
            Assert.AreEqual("/example/project/_apis/pipelines/12/runs/34", request.RequestUri!.AbsolutePath);
            return TransportTests.Json("""
                {"id":34,"pipeline":{"id":12},"variables":{"secret":"secret-sentinel"},"finalYaml":"secret-sentinel",
                 "resources":{"containers":{"secret":"secret-sentinel"},"repositories":{
                   "self":{"repository":{"type":"azureReposGit","url":"secret-sentinel"},"refName":"refs/heads/main","version":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"},
                   "shared-token":{"repository":{"type":"gitHub","credentials":"secret-sentinel"},"refName":"refs/tags/v1","version":"bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb","url":"secret-sentinel"}
                 }}}
                """);
        });
        using var http = new HttpClient(handler);
        using var auth = new TokenAuthentication("pat", new("token"));
        var result = await new PipelinesClient(new(http, auth, "example"), "example", "project").RunGetAsync(12, 34, CancellationToken.None);
        var provenance = result.Items[0].RepositoryProvenance!;
        Assert.AreEqual("reported_versions", provenance.Status);
        Assert.AreEqual(2, provenance.Repositories!.Count);
        Assert.AreEqual(new string('b', 40), provenance.Repositories[1].Version);
        Assert.AreEqual("refs/tags/v1", provenance.Repositories[1].RefName);
        Assert.AreEqual("shared-[REDACTED]", provenance.Repositories[1].Alias);
        Assert.IsFalse(JsonSerializer.Serialize(result).Contains("secret-sentinel", StringComparison.Ordinal));
        Assert.AreEqual(1, handler.Calls);
    }

    [TestMethod]
    [DataRow("{}", "unavailable", -1)]
    [DataRow("{\"resources\":null}", "unavailable", -1)]
    [DataRow("{\"resources\":{}}", "unavailable", -1)]
    [DataRow("{\"resources\":{\"repositories\":{}}}", "no_resources_reported", 0)]
    [DataRow("{\"resources\":{\"repositories\":{\"self\":{\"refName\":\"refs/heads/main\"}}}}", "versions_unavailable", 1)]
    public async Task MissingVersionsStayUnavailable(string properties, string status, int count)
    {
        string json = "{\"id\":34,\"pipeline\":{\"id\":12}" + (properties == "{}" ? "}" : "," + properties[1..]);
        using var handler = new TransportTests.FakeHandler(_ => TransportTests.Json(json));
        using var http = new HttpClient(handler);
        using var auth = new TokenAuthentication("pat", new("token"));
        var result = await new PipelinesClient(new(http, auth, "example"), "example", "project").RunGetAsync(12, 34, CancellationToken.None);
        var provenance = result.Items[0].RepositoryProvenance!;
        Assert.AreEqual(status, provenance.Status);
        Assert.AreEqual(count, provenance.Repositories?.Count ?? -1);
        if (count == 1) Assert.IsNull(provenance.Repositories![0].Version);
    }

    [TestMethod]
    [DataRow("[]")]
    [DataRow("{\"repositories\":[]}")]
    [DataRow("{\"repositories\":{\"self\":null}}")]
    [DataRow("{\"repositories\":{\"self\":{},\"self\":{}}}")]
    [DataRow("{\"repositories\":{\"self\":{\"repository\":[]}}}")]
    [DataRow("{\"repositories\":{\"self\":{\"version\":123}}}")]
    public async Task MalformedRepositoryResourcesFailSafely(string resources)
    {
        using var handler = new TransportTests.FakeHandler(_ => TransportTests.Json("{\"id\":34,\"pipeline\":{\"id\":12},\"resources\":" + resources + "}"));
        using var http = new HttpClient(handler);
        using var auth = new TokenAuthentication("pat", new("token"));
        var error = await Assert.ThrowsExactlyAsync<AdoException>(() => new PipelinesClient(new(http, auth, "example"), "example", "project").RunGetAsync(12, 34, CancellationToken.None));
        Assert.AreEqual("invalid_service_response", error.Code);
    }
}
