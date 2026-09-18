using System.IO.Compression;
using System.Net;
using Ado.Application;
using Ado.Domain;
using Ado.Infrastructure;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Ado.Tests;

[TestClass]
public sealed class BuildDownloadTests
{
    internal static byte[] Zip()
    {
        using var buffer = new MemoryStream();
        using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        using (var writer = new StreamWriter(zip.CreateEntry("../untrusted.txt").Open())) writer.Write("file contents");
        return buffer.ToArray();
    }
    internal static HttpResponseMessage Content(byte[] bytes) => new(HttpStatusCode.OK)
    { Content = new ByteArrayContent(bytes) { Headers = { ContentType = new("application/zip") } } };

    [TestMethod]
    [DataRow("store.vsblob.vsassets.io", false)]
    [DataRow("artprodcus3.artifacts.visualstudio.com", false)]
    [DataRow("artprodcus3.artifacts.visualstudio.com", true)]
    public async Task RedirectedZipUsesNoCredentialAndPublishesOnlyArchive(string redirectHost, bool redirectAgain)
    {
        using var directory = new DownloadDirectory();
        byte[] zip = Zip();
        using var service = new TransportTests.FakeHandler(request =>
        {
            Assert.AreEqual("Basic", request.Headers.Authorization!.Scheme);
            Assert.AreEqual("application/zip", request.Headers.Accept.Single().MediaType);
            Assert.AreEqual("https://dev.azure.com/example/Project/_apis/build/builds/34/artifacts?api-version=7.1&artifactName=drop", request.RequestUri!.AbsoluteUri);
            return new(HttpStatusCode.Redirect) { Headers = { Location = new($"https://{redirectHost}/content?sig=secret-sentinel") } };
        });
        int contentCalls = 0;
        using var storage = new TransportTests.FakeHandler(request =>
        {
            Assert.IsNull(request.Headers.Authorization);
            Assert.IsNull(request.Headers.Referrer);
            Assert.IsFalse(request.Headers.Contains("Cookie"));
            Assert.AreEqual("application/zip", request.Headers.Accept.Single().ToString());
            Assert.IsFalse(request.Headers.Contains("X-TFS-FedAuthRedirect"));
            contentCalls++;
            Assert.AreEqual(contentCalls == 1 ? redirectHost : "store.vsblob.vsassets.io", request.RequestUri!.Host);
            if (redirectAgain && contentCalls == 1)
                return new(HttpStatusCode.Redirect) { Headers = { Location = new("https://store.vsblob.vsassets.io/content?sig=secret-sentinel") } };
            return Content(zip);
        });
        using var serviceHttp = new HttpClient(service);
        using var contentHttp = new HttpClient(storage);
        using var auth = new TokenAuthentication("pat", new("synthetic"));
        var result = await new BuildArtifactDownloader(serviceHttp, contentHttp, auth, "example", "Project")
            .DownloadAsync(34, "drop", DownloadTarget.Validate(directory.Target), 10000, 10, CancellationToken.None);
        Assert.AreEqual(zip.Length, result.Bytes);
        Assert.AreEqual(redirectAgain ? 2 : 1, contentCalls);
        CollectionAssert.AreEqual(zip, await File.ReadAllBytesAsync(directory.Target));
        Assert.AreEqual(1, Directory.GetFiles(directory.Root).Length);
        Assert.AreEqual(0, Directory.GetDirectories(directory.Root).Length);
        Assert.IsFalse(result.ToString().Contains("secret-sentinel", StringComparison.Ordinal));
    }

    [TestMethod]
    [DataRow("http://store.vsblob.vsassets.io/file")]
    [DataRow("https://store.vsblob.vsassets.io.evil.invalid/file")]
    [DataRow("https://127.0.0.1/file")]
    [DataRow("https://store.blob.core.windows.net:444/file")]
    [DataRow("https://user@store.blob.core.windows.net/file")]
    [DataRow("https://dev.azure.com/other/_apis/build/builds")]
    [DataRow("https://artprodcus3.artifacts.visualstudio.com.evil.invalid/file")]
    [DataRow("https://fakeartifacts.visualstudio.com/file")]
    [DataRow("https://unrelated.visualstudio.com/file")]
    [DataRow("http://artprodcus3.artifacts.visualstudio.com/file")]
    [DataRow("https://artprodcus3.artifacts.visualstudio.com:444/file")]
    [DataRow("https://user@artprodcus3.artifacts.visualstudio.com/file")]
    [DataRow("https://artprodcus3.artifacts.visualstudio.com/file#fragment")]
    public void UnsafeStorageDestinationsAreRejected(string uri) =>
        Assert.ThrowsExactly<AdoException>(() => BuildArtifactDownloader.ValidateStorageDestination(new(uri)));

    [TestMethod]
    [DataRow("evil.invalid", "evil.invalid")]
    [DataRow("synthetic.evil.invalid", "[REDACTED].evil.invalid")]
    public async Task BlockedRedirectDoesNotSendStorageRequest(string host, string expectedHost)
    {
        using var directory = new DownloadDirectory();
        using var service = new TransportTests.FakeHandler(_ => new(HttpStatusCode.Redirect)
        { Headers = { Location = new($"https://secret-sentinel@{host}/secret-sentinel?sig=secret-sentinel#secret-sentinel") } });
        using var storage = new TransportTests.FakeHandler(_ => throw new AssertFailedException());
        using var serviceHttp = new HttpClient(service);
        using var contentHttp = new HttpClient(storage);
        using var auth = new TokenAuthentication("pat", new("synthetic"));
        var error = await Assert.ThrowsExactlyAsync<AdoException>(() => new BuildArtifactDownloader(serviceHttp, contentHttp, auth, "example", "Project")
            .DownloadAsync(34, "drop", DownloadTarget.Validate(directory.Target), 10000, 10, CancellationToken.None));
        Assert.AreEqual("unsafe_download_destination", error.Code);
        Assert.IsTrue(error.Message.Contains($"Redirect host: {expectedHost}.", StringComparison.Ordinal));
        Assert.IsFalse(error.Message.Contains("synthetic", StringComparison.Ordinal));
        Assert.IsFalse(error.Message.Contains("secret-sentinel", StringComparison.Ordinal));
        Assert.AreEqual(0, storage.Calls);
        Assert.AreEqual(0, Directory.GetFiles(directory.Root).Length);
    }

    [TestMethod]
    [DataRow("limit")]
    [DataRow("truncated")]
    [DataRow("invalidZip")]
    [DataRow("json")]
    [DataRow("serverError")]
    [DataRow("chunkedLimit")]
    public async Task FailureNeverPublishesOrLeavesPartialFiles(string mode)
    {
        using var directory = new DownloadDirectory();
        byte[] zip = Zip();
        using var handler = new TransportTests.FakeHandler(_ =>
        {
            var response = Content(mode == "invalidZip" ? new byte[30] : zip);
            if (mode == "truncated") response.Content.Headers.ContentLength = zip.Length + 10;
            if (mode == "json") response.Content.Headers.ContentType = new("application/json");
            if (mode == "serverError") response.StatusCode = HttpStatusCode.ServiceUnavailable;
            if (mode == "chunkedLimit") response.Content = new ChunkedContent(zip) { Headers = { ContentType = new("application/zip") } };
            return response;
        });
        using var http = new HttpClient(handler);
        using var auth = new TokenAuthentication("pat", new("synthetic"));
        await Assert.ThrowsExactlyAsync<AdoException>(() => new BuildArtifactDownloader(http, http, auth, "example", "Project")
            .DownloadAsync(34, "drop", DownloadTarget.Validate(directory.Target), mode is "limit" or "chunkedLimit" ? 30 : 10000, 10, CancellationToken.None));
        Assert.AreEqual(1, handler.Calls);
        Assert.AreEqual(0, Directory.GetFiles(directory.Root).Length);
    }

    [TestMethod]
    public async Task ConcurrentDestinationCreationNeverOverwrites()
    {
        using var directory = new DownloadDirectory();
        var target = DownloadTarget.Validate(directory.Target);
        using var handler = new TransportTests.FakeHandler(_ =>
        {
            File.WriteAllText(directory.Target, "existing");
            return Content(Zip());
        });
        using var http = new HttpClient(handler);
        using var auth = new TokenAuthentication("pat", new("synthetic"));
        await Assert.ThrowsExactlyAsync<AdoException>(() => new BuildArtifactDownloader(http, http, auth, "example", "Project")
            .DownloadAsync(34, "drop", target, 10000, 10, CancellationToken.None));
        Assert.AreEqual("existing", await File.ReadAllTextAsync(directory.Target));
        Assert.AreEqual(1, Directory.GetFiles(directory.Root).Length);
    }

    [TestMethod]
    public async Task DeadlineDoesNotPublishAFile()
    {
        using var directory = new DownloadDirectory();
        using var http = new HttpClient(new WaitingHandler());
        using var auth = new TokenAuthentication("pat", new("synthetic"));
        var error = await Assert.ThrowsExactlyAsync<AdoException>(() => new BuildArtifactDownloader(http, http, auth, "example", "Project")
            .DownloadAsync(34, "drop", DownloadTarget.Validate(directory.Target), 10000, 1, CancellationToken.None));
        Assert.AreEqual("download_timeout", error.Code);
        Assert.AreEqual(0, Directory.GetFiles(directory.Root).Length);
    }

    [TestMethod]
    public void ExistingDestinationAndMissingParentAreRejected()
    {
        using var directory = new DownloadDirectory();
        File.WriteAllText(directory.Target, "existing");
        Assert.ThrowsExactly<AdoException>(() => DownloadTarget.Validate(directory.Target));
        Assert.ThrowsExactly<AdoException>(() => DownloadTarget.Validate(Path.Combine(directory.Root, "missing", "out.zip")));
    }

    [TestMethod]
    public async Task InterruptedBodyCleansPartialAndSanitizesError()
    {
        using var directory = new DownloadDirectory();
        using var handler = new TransportTests.FakeHandler(_ => new(HttpStatusCode.OK)
        { Content = new StreamContent(new InterruptedStream(Zip())) { Headers = { ContentType = new("application/zip") } } });
        using var http = new HttpClient(handler);
        using var auth = new TokenAuthentication("pat", new("synthetic"));
        var error = await Assert.ThrowsExactlyAsync<AdoException>(() => new BuildArtifactDownloader(http, http, auth, "example", "Project")
            .DownloadAsync(34, "drop", DownloadTarget.Validate(directory.Target), 10000, 10, CancellationToken.None));
        Assert.IsFalse(error.Message.Contains("secret-sentinel", StringComparison.Ordinal));
        Assert.AreEqual(0, Directory.GetFiles(directory.Root).Length);
    }

    [TestMethod]
    public async Task RedirectLoopsHaveFixedRequestCeiling()
    {
        using var directory = new DownloadDirectory();
        using var handler = new TransportTests.FakeHandler(_ => new(HttpStatusCode.Redirect)
        { Headers = { Location = new("https://store.blob.core.windows.net/blob?sig=secret-sentinel") } });
        using var http = new HttpClient(handler);
        using var auth = new TokenAuthentication("pat", new("synthetic"));
        await Assert.ThrowsExactlyAsync<AdoException>(() => new BuildArtifactDownloader(http, http, auth, "example", "Project")
            .DownloadAsync(34, "drop", DownloadTarget.Validate(directory.Target), 10000, 10, CancellationToken.None));
        Assert.AreEqual(6, handler.Calls);
        Assert.AreEqual(0, Directory.GetFiles(directory.Root).Length);
    }

    [TestMethod]
    public void SymlinkAncestorIsRejectedWhenPlatformPermitsCreation()
    {
        using var directory = new DownloadDirectory();
        string real = Path.Combine(directory.Root, "real");
        string link = Path.Combine(directory.Root, "link");
        Directory.CreateDirectory(real);
        try { Directory.CreateSymbolicLink(link, real); }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or PlatformNotSupportedException)
        { Assert.Inconclusive("Creating a synthetic symlink is unavailable on this platform/session."); }
        Assert.ThrowsExactly<AdoException>(() => DownloadTarget.Validate(Path.Combine(link, "out.zip")));
    }

    private sealed class InterruptedStream(byte[] bytes) : MemoryStream(bytes)
    {
        private bool read;
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (read) return ValueTask.FromException<int>(new IOException("secret-sentinel"));
            read = true;
            return base.ReadAsync(buffer, cancellationToken);
        }
    }

    private sealed class ChunkedContent(byte[] bytes) : HttpContent
    {
        protected override bool TryComputeLength(out long length) { length = 0; return false; }
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) => stream.WriteAsync(bytes).AsTask();
    }
    private sealed class WaitingHandler : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        { await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken); throw new AssertFailedException(); }
    }
}

internal sealed class DownloadDirectory : IDisposable
{
    public string Root { get; } = Path.Combine(AppContext.BaseDirectory, "download-test-" + Guid.NewGuid().ToString("N"));
    public string Target => Path.Combine(Root, "output.zip");
    public DownloadDirectory() => Directory.CreateDirectory(Root);
    public void Dispose() => Directory.Delete(Root, recursive: true);
}
