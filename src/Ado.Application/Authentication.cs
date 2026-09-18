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
    IReadOnlyList<string> RedactLines(IReadOnlyList<string> lines);
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
    public string Redact(string text) => text.Replace(Convert.ToBase64String(Encoding.UTF8.GetBytes(":" + secret.Read())), "[REDACTED]", StringComparison.Ordinal)
        .Replace(secret.Read(), "[REDACTED]", StringComparison.Ordinal);
    public IReadOnlyList<string> RedactLines(IReadOnlyList<string> lines)
    {
        // Match both chunked strings and logical LF/CRLF lines before applying output bounds.
        // Preserve every original array element and redact only the matched characters.
        var masks = lines.Select(line => new bool[line.Length]).ToArray();
        string[] secrets = [secret.Read(), Convert.ToBase64String(Encoding.UTF8.GetBytes(":" + secret.Read()))];
        foreach (string separator in new[] { "", "\n", "\r\n" })
        {
            string joined = string.Join(separator, lines);
            var matched = new bool[joined.Length];
            foreach (string token in secrets)
                for (int start = 0; start <= joined.Length - token.Length;)
                {
                    int at = joined.IndexOf(token, start, StringComparison.Ordinal);
                    if (at < 0) break;
                    Array.Fill(matched, true, at, token.Length);
                    start = at + token.Length;
                }
            int offset = 0;
            for (int i = 0; i < lines.Count; i++)
            {
                for (int j = 0; j < lines[i].Length; j++) masks[i][j] |= matched[offset + j];
                offset += lines[i].Length + separator.Length;
            }
        }
        return lines.Select((line, i) =>
        {
            var output = new StringBuilder();
            for (int j = 0; j < line.Length; j++)
                if (!masks[i][j]) output.Append(line[j]);
                else if (j == 0 || !masks[i][j - 1]) output.Append("[REDACTED]");
            return output.ToString();
        }).ToArray();
    }
    public void Dispose() => secret.Dispose();
}
