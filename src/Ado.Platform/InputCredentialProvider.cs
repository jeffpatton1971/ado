using System.Text;
using Ado.Application;
using Ado.Domain;

namespace Ado.Platform;

public sealed class InputCredentialProvider(Func<string, string?> environment, TextReader input,
    Func<CancellationToken, Task<string>> prompt) : ICredentialProvider
{
    public async Task<SecretValue> GetAsync(CredentialReference reference, bool nonInteractive, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string value;
        switch (reference.Provider)
        {
            case "environment":
                value = environment("ADO_TOKEN") ?? throw new AdoException("credential_missing", "ADO_TOKEN is not set. Supply a credential through stdin or a credential provider.", ExitCode.Authentication);
                break;
            case "stdin":
                var buffer = new char[16387];
                int count = 0;
                try
                {
                    while (count < buffer.Length)
                    {
                        int read = await input.ReadAsync(buffer.AsMemory(count), cancellationToken);
                        if (read == 0) break;
                        count += read;
                    }
                    if (count == buffer.Length) throw new AdoException("invalid_credential", "Credential input exceeds its limit.", ExitCode.Authentication);
                    if (count > 0 && buffer[count - 1] == '\n')
                    {
                        count--;
                        if (count > 0 && buffer[count - 1] == '\r') count--;
                    }
                    value = new string(buffer, 0, count);
                    if (value.Contains('\r') || value.Contains('\n'))
                        throw new AdoException("invalid_credential", "Token stdin accepts one line, optionally followed by a line ending.", ExitCode.Authentication);
                }
                finally { Array.Clear(buffer); }
                break;
            case "prompt":
                if (nonInteractive) throw new AdoException("interaction_required", "A token prompt is unavailable in non-interactive or JSON mode.", ExitCode.Authentication);
                value = await prompt(cancellationToken);
                break;
            default:
                throw new AdoException("credential_provider_unavailable", "The requested credential backend is unavailable.", ExitCode.Authentication);
        }
        return new SecretValue(value);
    }

    public static async Task<string> PromptAsync(TextWriter error, CancellationToken cancellationToken)
    {
        if (Console.IsInputRedirected || Console.IsErrorRedirected)
            throw new AdoException("interaction_required", "A terminal is required for masked token input; use --token-stdin instead.", ExitCode.Authentication);
        await error.WriteAsync("Token: ");
        var value = new StringBuilder();
        try
        {
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!Console.KeyAvailable) { await Task.Delay(25, cancellationToken); continue; }
                var key = Console.ReadKey(intercept: true);
                if (key.Key == ConsoleKey.Enter) return value.ToString();
                if (key.Key == ConsoleKey.Backspace)
                {
                    if (value.Length > 0) { value.Length--; await error.WriteAsync("\b \b"); }
                }
                else if (!char.IsControl(key.KeyChar))
                {
                    if (value.Length >= 16384) throw new AdoException("invalid_credential", "Credential input exceeds its limit.", ExitCode.Authentication);
                    value.Append(key.KeyChar);
                    await error.WriteAsync("*");
                }
            }
        }
        finally { value.Clear(); await error.WriteLineAsync(); }
    }
}
