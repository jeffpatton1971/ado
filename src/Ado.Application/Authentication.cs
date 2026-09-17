using System.Net.Http.Headers;
using System.Text;
using Ado.Domain;

namespace Ado.Application;

public interface ICredentialProvider
{
    Task<SecretValue> GetAsync(CredentialReference reference, bool nonInteractive, CancellationToken cancellationToken);
}

// No public secret property: accidental serialization and ToString cannot expose it.
public sealed class SecretValue : IDisposable
{
    private string? value;
    public SecretValue(string value)
    {
        if (value.Length is 0 or > 16384)
            throw new AdoException("invalid_credential", "The credential is empty or exceeds the input limit.", ExitCode.Authentication);
        this.value = value;
    }
    internal string Read() => value ?? throw new ObjectDisposedException(nameof(SecretValue));
    public override string ToString() => "[REDACTED]";
    public void Dispose() => value = null; // Managed strings cannot promise secure erasure.
}

public interface IAuthenticationProvider : IDisposable
{
    void Apply(HttpRequestMessage request);
    string Redact(string text);
}

public sealed class TokenAuthentication : IAuthenticationProvider
{
    private readonly SecretValue secret;
    private readonly string type;
    public TokenAuthentication(string type, SecretValue secret)
    {
        if (type is not ("pat" or "entra-token"))
            throw new AdoException("invalid_auth_type", "Authentication type must be pat or entra-token.", ExitCode.Usage);
        this.type = type;
        this.secret = secret;
    }
    public void Apply(HttpRequestMessage request)
    {
        string value = secret.Read();
        if (type == "pat")
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes(":" + value)));
        else
        {
            if (value.Any(c => c is '\r' or '\n' or '\0'))
                throw new AdoException("invalid_credential", "The credential cannot be represented safely in an HTTP header.", ExitCode.Authentication);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", value);
        }
    }
    public string Redact(string text) => text.Replace(secret.Read(), "[REDACTED]", StringComparison.Ordinal)
        .Replace(Convert.ToBase64String(Encoding.UTF8.GetBytes(":" + secret.Read())), "[REDACTED]", StringComparison.Ordinal);
    public void Dispose() => secret.Dispose();
}
