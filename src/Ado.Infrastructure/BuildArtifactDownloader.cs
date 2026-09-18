using System.Net;
using Ado.Application;
using Ado.Domain;
using Ado.Infrastructure.Http;

namespace Ado.Infrastructure;

public sealed record BuildOutputDownload(int BuildId, string ArtifactName, string Destination, long Bytes, string Format = "zip");

public sealed class BuildArtifactDownloader(HttpClient serviceClient, HttpClient contentClient, IAuthenticationProvider authentication,
    string organization, string project)
{
    public static HttpClient CreateClient() => new(new HttpClientHandler
    {
        AllowAutoRedirect = false,
        UseCookies = false,
        UseDefaultCredentials = false,
        AutomaticDecompression = DecompressionMethods.None
    })
    { Timeout = Timeout.InfiniteTimeSpan };

    public static void ValidateStorageDestination(Uri uri)
    {
        string[] suffixes = [".vsblob.vsassets.io", ".vsblob.visualstudio.com", ".blob.core.windows.net", ".dedup.microsoft.com"];
        if (!uri.IsAbsoluteUri || uri.Scheme != "https" || !uri.IsDefaultPort || uri.UserInfo.Length != 0 || uri.Fragment.Length != 0
            || uri.HostNameType != UriHostNameType.Dns || uri.AbsoluteUri.Length > 16384
            || !suffixes.Any(suffix => uri.Host.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)))
            throw new AdoException("unsafe_download_destination", "The download redirect is outside the allowed HTTPS artifact-storage hosts.", ExitCode.Safety);
    }

    public async Task<BuildOutputDownload> DownloadAsync(int buildId, string artifactName, DownloadTarget target,
        long maxBytes, int timeoutSeconds, CancellationToken cancellationToken)
    {
        if (maxBytes <= 0 || timeoutSeconds <= 0 || timeoutSeconds > 86400)
            throw new AdoException("invalid_download_bounds", "Download byte limit and timeout must be positive; timeout cannot exceed one day.", ExitCode.Usage);
        var uri = EndpointBuilder.BuildArtifact(Operations.BuildArtifactGet, organization, project, buildId, artifactName);
        EndpointBuilder.ValidateDestination(uri, ServiceHost.Core, organization, project);
        target.Check();
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));
        string? temporary = null;
        try
        {
            for (int hop = 0; hop <= 5; hop++)
            {
                deadline.Token.ThrowIfCancellationRequested();
                if (hop > 0) ValidateRedirect(uri);
                using var request = new HttpRequestMessage(HttpMethod.Get, uri);
                request.Headers.Accept.ParseAdd("application/zip");
                if (hop == 0)
                {
                    authentication.Apply(request);
                    request.Headers.Add("X-TFS-FedAuthRedirect", "Suppress");
                }
                // The content client has no cookies/default credentials and never receives authentication.
                using var response = await (hop == 0 ? serviceClient : contentClient).SendAsync(request, HttpCompletionOption.ResponseHeadersRead, deadline.Token);
                if (response.StatusCode is HttpStatusCode.MovedPermanently or HttpStatusCode.Redirect or HttpStatusCode.SeeOther or HttpStatusCode.TemporaryRedirect or HttpStatusCode.PermanentRedirect)
                {
                    if (hop == 5 || response.Headers.Location is not { } location) throw Failure();
                    uri = location.IsAbsoluteUri ? location : new Uri(uri, location);
                    ValidateRedirect(uri);
                    continue;
                }
                if (response.StatusCode == HttpStatusCode.Unauthorized) throw new AdoException("authentication_failed", "The download request was rejected or its signed access expired.", ExitCode.Authentication);
                if (response.StatusCode == HttpStatusCode.Forbidden) throw new AdoException("authorization_failed", "Access to the build output was refused.", ExitCode.Authorization);
                if (response.StatusCode == HttpStatusCode.NotFound) throw new AdoException("resource_not_found", "The build output was not found or is no longer available.", ExitCode.NotFound);
                if (response.StatusCode != HttpStatusCode.OK) throw Failure();
                if (response.Content.Headers.ContentEncoding.Count != 0) throw Failure();
                string? mediaType = response.Content.Headers.ContentType?.MediaType;
                if (mediaType is not ("application/zip" or "application/octet-stream" or "application/x-zip-compressed"))
                    throw new AdoException("unsupported_download_response", "The service did not return a supported ZIP response. This resource may require a different download protocol.", ExitCode.Safety);
                long? expected = response.Content.Headers.ContentLength;
                if (expected > maxBytes) throw TooLarge();
                target.Check();
                string candidate = target.TemporaryPath();
                var options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.ReadWrite, Share = FileShare.None, Options = FileOptions.Asynchronous };
                if (!OperatingSystem.IsWindows()) options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
                long bytes = 0;
                var createdFile = new FileStream(candidate, options);
                temporary = candidate; // Cleanup only a file successfully created by this operation.
                await using (var file = createdFile)
                {
                    await using var stream = await response.Content.ReadAsStreamAsync(deadline.Token);
                    var buffer = new byte[65536];
                    while (true)
                    {
                        int read = await stream.ReadAsync(buffer, deadline.Token);
                        if (read == 0) break;
                        if (read > maxBytes - bytes) throw TooLarge();
                        await file.WriteAsync(buffer.AsMemory(0, read), deadline.Token);
                        bytes += read;
                    }
                    if (expected is not null && bytes != expected) throw Failure();
                    await file.FlushAsync(deadline.Token);
                    await CheckZipEnvelopeAsync(file, deadline.Token);
                }
                deadline.Token.ThrowIfCancellationRequested();
                target.Check();
                File.Move(temporary, target.Path, overwrite: false);
                temporary = null;
                return new(buildId, authentication.Redact(artifactName), authentication.Redact(target.Path), bytes);
            }
            throw Failure();
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        { throw new AdoException("download_timeout", "The download exceeded its deadline; no completed file was published.", ExitCode.Transient); }
        catch (Exception ex) when (ex is HttpRequestException or IOException or UnauthorizedAccessException or UriFormatException)
        { throw Failure(); }
        finally
        {
            if (temporary is not null)
                try { File.Delete(temporary); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
    }

    private void ValidateRedirect(Uri uri)
    {
        try { ValidateStorageDestination(uri); }
        catch (AdoException ex) when (ex.Code == "unsafe_download_destination")
        {
            // Never include the signed path, query, user information or fragment.
            string host = uri.IsAbsoluteUri && uri.HostNameType == UriHostNameType.Dns ? uri.IdnHost : "";
            string diagnostic = host.Length is > 0 and <= 253
                && host.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '-')
                ? authentication.Redact(host) : "unavailable";
            throw new AdoException(ex.Code, $"{ex.Message} Redirect host: {diagnostic}.", ExitCode.Safety);
        }
    }

    private static async Task CheckZipEnvelopeAsync(FileStream file, CancellationToken cancellationToken)
    {
        if (file.Length < 22) throw Failure();
        file.Position = 0;
        var header = new byte[4];
        await file.ReadExactlyAsync(header, cancellationToken);
        if (header[0] != 0x50 || header[1] != 0x4b || !((header[2] == 3 && header[3] == 4) || (header[2] == 5 && header[3] == 6))) throw Failure();
        var tail = new byte[(int)Math.Min(file.Length, 65557)];
        file.Position = file.Length - tail.Length;
        await file.ReadExactlyAsync(tail, cancellationToken);
        for (int i = tail.Length - 22; i >= 0; i--)
            if (tail[i] == 0x50 && tail[i + 1] == 0x4b && tail[i + 2] == 5 && tail[i + 3] == 6
                && i + 22 + tail[i + 20] + (tail[i + 21] << 8) == tail.Length) return;
        throw Failure(); // Envelope check only; no extraction, entry parsing or CRC/integrity guarantee.
    }
    private static AdoException TooLarge() => new("download_limit_exceeded", "The download exceeds its byte limit; no completed file was published.", ExitCode.Partial);
    private static AdoException Failure() => new("download_failed", "The ZIP download could not be completed and verified. No automatic retry was attempted; check the destination and service availability.", ExitCode.Transient);
}
