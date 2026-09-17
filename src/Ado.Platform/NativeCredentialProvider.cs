using Ado.Application;
using Ado.Domain;

namespace Ado.Platform;

public interface INativeCredentialStore
{
    Task<string> ReadAsync(string service, string account, bool nonInteractive, CancellationToken cancellationToken);
}

public sealed class NativeCredentialProvider(IReadOnlyDictionary<string, INativeCredentialStore> stores) : ICredentialProvider
{
    public async Task<SecretValue> GetAsync(CredentialReference reference, bool nonInteractive, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(reference.Service) || string.IsNullOrEmpty(reference.Account)
            || reference.Service.Contains('\0') || reference.Account.Contains('\0'))
            throw new AdoException("invalid_credential_reference", "Native credentials require valid service and account lookup keys.", ExitCode.Configuration);
        if (!stores.TryGetValue(reference.Provider, out var store))
            throw new AdoException("credential_provider_unavailable", "This native credential provider is unavailable on this platform. Use stdin or environment injection.", ExitCode.Authentication);
        try
        {
            string value = await store.ReadAsync(reference.Service, reference.Account, nonInteractive, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            return new(value);
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException)
        {
            throw new AdoException("credential_provider_unavailable", "The native credential library is unavailable. Install the OS backend or use stdin/environment injection.", ExitCode.Authentication);
        }
    }

    public static NativeCredentialProvider CreateDefault()
    {
        var stores = new Dictionary<string, INativeCredentialStore>(StringComparer.Ordinal);
        if (OperatingSystem.IsWindows()) stores.Add("windows-credential-manager", new WindowsCredentialStore());
        else if (OperatingSystem.IsMacOS()) stores.Add("macos-keychain", new MacCredentialStore());
        else if (OperatingSystem.IsLinux()) stores.Add("linux-secret-service", new LinuxCredentialStore());
        return new(stores);
    }
}

public static class NativeCredentialErrors
{
    public static AdoException Windows(int code) => code switch
    {
        1168 => new("credential_not_found", "No matching Windows generic credential exists. Check its Internet/network address (service) and user name (account).", ExitCode.Authentication),
        1312 => new("credential_provider_unavailable", "Windows Credential Manager is unavailable in this logon session. Use stdin/environment injection in unattended sessions.", ExitCode.Authentication),
        _ => new("credential_access_denied", "Windows Credential Manager lookup failed. Check the user session and credential access.", ExitCode.Authentication)
    };

    public static AdoException Mac(int code) => code switch
    {
        -25300 => new("credential_not_found", "No matching Keychain generic password exists. Create the item with the configured service and account.", ExitCode.Authentication),
        -25308 => new("interaction_required", "Keychain requires access approval or unlocking. Run interactively, approve the executable with Always Allow, then verify --non-interactive. Rebuilding or relocating it may require approval again.", ExitCode.Authentication),
        _ => new("credential_access_denied", "Keychain access was denied or cancelled. Unlock the keychain and approve the requesting executable interactively before unattended use.", ExitCode.Authentication)
    };
}
