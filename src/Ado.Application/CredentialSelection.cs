using Ado.Domain;

namespace Ado.Application;

public sealed record CredentialSelection(CredentialReference Reference, bool OverridesProfile)
{
    public static CredentialSelection Resolve(CredentialReference configured, string? type, bool tokenArgument,
        bool tokenStdin, bool tokenPrompt, string? provider, string? service, string? account,
        Func<string, string?> environment)
    {
        int explicitSources = (tokenArgument ? 1 : 0) + (tokenStdin ? 1 : 0) + (tokenPrompt ? 1 : 0) + (provider is not null ? 1 : 0);
        if (explicitSources > 1) throw Usage("Choose exactly one explicit credential source.");
        if (provider is null && (service is not null || account is not null))
            throw Usage("Explicit credential service/account require --credential-provider.");
        string authType = type ?? environment("ADO_AUTH_TYPE") ?? configured.Type;
        if (authType is not ("pat" or "entra-token")) throw Usage("Authentication type must be pat or entra-token.");
        CredentialReference selected;
        bool overridden = explicitSources > 0;
        if (overridden)
            selected = new() { Type = authType, Provider = tokenArgument ? "argument" : tokenStdin ? "stdin" : tokenPrompt ? "prompt" : provider!, Service = service, Account = account };
        else
        {
            string? envProvider = environment("ADO_CREDENTIAL_PROVIDER");
            // Environment injection takes precedence over profile references, but never over explicit sources.
            if (envProvider is not null)
            {
                selected = new() { Type = authType, Provider = envProvider, Service = environment("ADO_CREDENTIAL_SERVICE"), Account = environment("ADO_CREDENTIAL_ACCOUNT") };
                overridden = true;
            }
            else if (environment("ADO_TOKEN") is not null)
            {
                selected = new() { Type = authType, Provider = "environment" };
                overridden = true;
            }
            else selected = configured with { Type = authType };
        }
        if (selected.Provider is not ("argument" or "environment" or "stdin" or "prompt" or "windows-credential-manager" or "macos-keychain" or "linux-secret-service"))
            throw Usage("Unknown credential provider.");
        if (selected.Provider == "argument" && !tokenArgument) throw Usage("Use --token to select argument credentials.");
        if (selected.Provider is "windows-credential-manager" or "macos-keychain" or "linux-secret-service")
            if (string.IsNullOrEmpty(selected.Service) || string.IsNullOrEmpty(selected.Account))
                throw Usage("Native credential selection requires a complete service/account reference.");
        return new(selected, overridden);
    }
    private static AdoException Usage(string message) => new("invalid_credential_selection", message, ExitCode.Usage);
}
