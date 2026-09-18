using Ado.Application;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Ado.Tests;

[TestClass]
public sealed class BuildPullRequestAnalysisTests
{
    [TestMethod]
    [DataRow("GitHub")]
    [DataRow("TfsGit")]
    public void RecognizedPrReportsMergeWithoutInventingHead(string type)
    {
        var context = BuildPullRequestAnalysis.Analyze("pullRequest", "refs/pull/8/merge", new string('a', 40), type)!;
        Assert.AreEqual(8, context.Number);
        Assert.AreEqual("pull_request_reason_and_merge_ref", context.Evidence);
        Assert.AreEqual("reported_merge_commit", context.BuiltVersionKind);
        Assert.IsNull(context.HeadVersion);
        Assert.AreEqual("not_resolved", context.HeadVersionStatus);
    }

    [TestMethod]
    [DataRow("refs/pull/merge")]
    [DataRow("refs/pull//merge")]
    [DataRow("refs/pull/0/merge")]
    [DataRow("refs/pull/08/merge")]
    [DataRow("refs/pull/+8/merge")]
    [DataRow("refs/pull/2147483648/merge")]
    [DataRow("refs/pull/8/head")]
    [DataRow("refs/heads/feature/pr-8")]
    public void UnrecognizedRefsRetainReasonWithoutGuessingNumber(string branch)
    {
        var context = BuildPullRequestAnalysis.Analyze("pullRequest", branch, "revision", "GitHub")!;
        Assert.IsNull(context.Number);
        Assert.AreEqual("pull_request_reason_only", context.Evidence);
        Assert.AreEqual("unknown", context.BuiltVersionKind);
        Assert.IsNull(context.HeadVersion);
    }

    [TestMethod]
    public void RefAloneDoesNotEstablishPrTriggerOrMergeCommit()
    {
        var context = BuildPullRequestAnalysis.Analyze("manual", "refs/pull/8/merge", "revision", "GitHub")!;
        Assert.AreEqual(8, context.Number);
        Assert.AreEqual("merge_ref_only", context.Evidence);
        Assert.AreEqual("unknown", context.BuiltVersionKind);
        Assert.IsNull(BuildPullRequestAnalysis.Analyze("individualCI", "refs/heads/main", "revision", "GitHub"));
    }

    [TestMethod]
    public void UnknownProviderOrMissingVersionCannotEstablishMergeRevision()
    {
        var provider = BuildPullRequestAnalysis.Analyze("pullRequest", "refs/pull/8/merge", "revision", null)!;
        Assert.IsNull(provider.Number);
        Assert.AreEqual("unknown", provider.BuiltVersionKind);
        var missing = BuildPullRequestAnalysis.Analyze("pullRequest", "refs/pull/8/merge", null, "GitHub")!;
        Assert.AreEqual(8, missing.Number);
        Assert.AreEqual("unknown", missing.BuiltVersionKind);
    }
}
