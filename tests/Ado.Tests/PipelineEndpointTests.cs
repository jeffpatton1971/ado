using Ado.Application;
using Ado.Domain;
using Ado.Infrastructure.Http;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Ado.Tests;

[TestClass]
public sealed class PipelineEndpointTests
{
    [TestMethod]
    public void EachRegisteredPipelineEndpointPinsDocumentedRouteAndVersion()
    {
        var operations = new[] { Operations.PipelineList, Operations.PipelineGet, Operations.PipelineRuns, Operations.PipelineRunGet };
        var suffixes = new[] { "", "/12", "/12/runs", "/12/runs/34" };
        for (int i = 0; i < operations.Length; i++)
        {
            var uri = EndpointBuilder.Pipeline(operations[i], "example", "My Project", 12, 34);
            Assert.AreEqual("https://dev.azure.com/example/My%20Project/_apis/pipelines" + suffixes[i] + "?api-version=7.1", uri.AbsoluteUri);
            EndpointBuilder.ValidateDestination(uri, ServiceHost.Core, "example", "My Project");
            Assert.ThrowsExactly<AdoException>(() => EndpointBuilder.ValidateDestination(uri, ServiceHost.Core, "example", "Other Project"));
        }
    }

    [TestMethod]
    public void OpaqueContinuationIsEncodedAndNotAppliedToRuns()
    {
        var uri = EndpointBuilder.Pipeline(Operations.PipelineList, "example", "project", top: 5, continuation: "a+b/&x=1");
        StringAssert.Contains(uri.AbsoluteUri, "continuationToken=a%2Bb%2F%26x%3D1");
        Assert.ThrowsExactly<AdoException>(() => EndpointBuilder.Pipeline(Operations.PipelineRuns, "example", "project", 12, top: 5));
        Assert.ThrowsExactly<AdoException>(() => EndpointBuilder.Pipeline(Operations.PipelineRunGet, "example", "project", 12, 0));
    }
}
