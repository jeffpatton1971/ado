namespace Ado.Domain;

// Only developer-authored, secret-free messages belong in this exception.
public sealed class AdoException(string code, string safeMessage, ExitCode exitCode,
    bool retryable = false) : Exception(safeMessage)
{
    public string Code { get; } = code;
    public ExitCode ExitCode { get; } = exitCode;
    public bool Retryable { get; } = retryable;
}
