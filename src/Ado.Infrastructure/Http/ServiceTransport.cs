using System.Net;
using System.Text.Json;
using Ado.Application;
using Ado.Domain;

namespace Ado.Infrastructure.Http;

public sealed record JsonResponse(JsonDocument Document, string? ContinuationToken, string? RequestId, int Bytes) : IDisposable
{
    public void Dispose() => Document.Dispose();
}

public sealed class ServiceTransport(HttpClient client, IAuthenticationProvider authentication, string organization,
    int requestSeconds = 60, bool readOnly = true, bool dryRun = false,
    Func<TimeSpan, CancellationToken, Task>? delay = null)
{
    public static HttpClient CreateClient() => new(new HttpClientHandler
    {
        AllowAutoRedirect = false,
        UseCookies = false,
        UseDefaultCredentials = false,
        AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
    })
    { Timeout = Timeout.InfiniteTimeSpan };

    public string Redact(string text) => authentication.Redact(text);

    public async Task<JsonResponse> GetAsync(OperationDescriptor operation, Uri uri, CancellationToken cancellationToken)
    {
        SafetyPolicy.BeforeDispatch(operation, readOnly, dryRun);
        if (operation.IsWrite) throw new AdoException("unsupported_operation", "The read transport cannot send mutations.", ExitCode.Safety);
        EndpointBuilder.ValidateDestination(uri, operation.Service, organization);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(requestSeconds));
        try
        {
            for (int attempt = 0; attempt < 3; attempt++)
            {
                try
                {
                    using var request = new HttpRequestMessage(HttpMethod.Get, uri);
                    request.Headers.Accept.ParseAdd("application/json");
                    request.Headers.Add("X-TFS-FedAuthRedirect", "Suppress");
                    authentication.Apply(request);
                    using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, deadline.Token);
                    if (IsTransient(response.StatusCode))
                    {
                        if (attempt == 2) throw Transient();
                        var wait = response.Headers.RetryAfter?.Delta
                            ?? (response.Headers.RetryAfter?.Date is { } date ? date - DateTimeOffset.UtcNow : TimeSpan.FromMilliseconds(200 * (1 << attempt) + Random.Shared.Next(100)));
                        if (wait > TimeSpan.FromSeconds(requestSeconds)) throw Transient();
                        await (delay ?? Task.Delay)(wait < TimeSpan.Zero ? TimeSpan.Zero : wait, deadline.Token);
                        continue;
                    }
                    if (!response.IsSuccessStatusCode) throw Map(response.StatusCode);
                    const int maximum = 4 * 1024 * 1024;
                    if (response.Content.Headers.ContentLength > maximum) throw Oversized();
                    await using var stream = await response.Content.ReadAsStreamAsync(deadline.Token);
                    using var buffer = new MemoryStream();
                    var chunk = new byte[16384];
                    while (true)
                    {
                        int read = await stream.ReadAsync(chunk, deadline.Token);
                        if (read == 0) break;
                        if (buffer.Length + read > maximum) throw Oversized();
                        await buffer.WriteAsync(chunk.AsMemory(0, read), deadline.Token);
                    }
                    string? continuation = Header(response, "x-ms-continuationtoken");
                    if (response.Headers.Contains("x-ms-continuationtoken") && continuation is null)
                        throw new AdoException("invalid_service_response", "The project continuation token is invalid.", ExitCode.Transient);
                    if (continuation is not null && (!int.TryParse(continuation, out int offset) || offset < 0))
                        throw new AdoException("invalid_service_response", "The project continuation token is invalid.", ExitCode.Transient);
                    string? requestId = Header(response, "x-vss-e2eid") ?? Header(response, "x-ms-request-id");
                    return new(JsonDocument.Parse(buffer.ToArray(), new JsonDocumentOptions { MaxDepth = 32 }), continuation,
                        requestId is null ? null : Redact(requestId), checked((int)buffer.Length));
                }
                catch (HttpRequestException) when (attempt < 2)
                {
                    await (delay ?? Task.Delay)(TimeSpan.FromMilliseconds(200 * (1 << attempt) + Random.Shared.Next(100)), deadline.Token);
                }
                catch (HttpRequestException) { throw Transient(); }
            }
            throw Transient();
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        { throw new AdoException("request_timeout", "The read request exceeded its timeout.", ExitCode.Transient, true); }
        catch (JsonException)
        { throw new AdoException("invalid_service_response", "The service returned invalid or excessively nested JSON.", ExitCode.Transient); }
        catch (IOException) { throw Transient(); }
    }

    private static string? Header(HttpResponseMessage response, string name)
    {
        if (!response.Headers.TryGetValues(name, out var values)) return null;
        string? value = values.FirstOrDefault();
        return value is { Length: > 0 and <= 128 } && !value.Any(char.IsControl) ? value : null;
    }
    private static bool IsTransient(HttpStatusCode code) => code is HttpStatusCode.TooManyRequests or HttpStatusCode.InternalServerError or HttpStatusCode.BadGateway or HttpStatusCode.ServiceUnavailable or HttpStatusCode.GatewayTimeout or HttpStatusCode.RequestTimeout;
    private static AdoException Transient() => new("transient_service_failure", "The service is temporarily unavailable or rate limited; the bounded read attempts did not succeed.", ExitCode.Transient, true);
    private static AdoException Oversized() => new("response_limit_exceeded", "The service response exceeds the 4 MiB safety limit.", ExitCode.Partial);
    private static AdoException Map(HttpStatusCode code) => code switch
    {
        HttpStatusCode.Unauthorized => new("authentication_failed", "Azure DevOps rejected the supplied credential.", ExitCode.Authentication),
        HttpStatusCode.Forbidden => new("authorization_failed", "The credential lacks access to this operation or resource.", ExitCode.Authorization),
        HttpStatusCode.NotFound => new("resource_not_found", "The requested resource was not found or is not visible to this identity.", ExitCode.NotFound),
        HttpStatusCode.Conflict or HttpStatusCode.PreconditionFailed => new("conflict", "The request conflicts with the current resource state.", ExitCode.Safety),
        >= HttpStatusCode.MultipleChoices and < HttpStatusCode.BadRequest => new("redirect_refused", "The service redirected the request. Credentials were not forwarded; verify organization and authentication.", ExitCode.Safety),
        _ => new("service_request_failed", "Azure DevOps rejected the request. Check the command context and documented permissions.", ExitCode.Transient)
    };
}
