using System.Runtime.InteropServices;
using System.Text;
using Ado.Domain;

namespace Ado.Platform;

internal sealed class MacCredentialStore : INativeCredentialStore
{
    // User-interaction policy is process-wide in this native API.
    private static readonly SemaphoreSlim Gate = new(1);
    private const string Security = "/System/Library/Frameworks/Security.framework/Security";

    public async Task<string> ReadAsync(string service, string account, bool nonInteractive, CancellationToken cancellationToken)
    {
        await Gate.WaitAsync(cancellationToken);
        try
        {
            return await Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                int status = SecKeychainGetUserInteractionAllowed(out byte previous);
                if (status != 0) throw NativeCredentialErrors.Mac(status);
                status = SecKeychainSetUserInteractionAllowed(nonInteractive ? (byte)0 : (byte)1);
                if (status != 0) throw NativeCredentialErrors.Mac(status);
                IntPtr data = IntPtr.Zero;
                try
                {
                    var serviceBytes = Encoding.UTF8.GetBytes(service);
                    var accountBytes = Encoding.UTF8.GetBytes(account);
                    status = SecKeychainFindGenericPassword(IntPtr.Zero, (uint)serviceBytes.Length, serviceBytes,
                        (uint)accountBytes.Length, accountBytes, out uint length, out data, IntPtr.Zero);
                    if (status != 0) throw NativeCredentialErrors.Mac(status);
                    if (length > 65536) throw new AdoException("invalid_credential", "Keychain credential exceeds the input limit.", ExitCode.Authentication);
                    cancellationToken.ThrowIfCancellationRequested();
                    return Marshal.PtrToStringUTF8(data, checked((int)length));
                }
                finally
                {
                    if (data != IntPtr.Zero) SecKeychainItemFreeContent(IntPtr.Zero, data);
                    SecKeychainSetUserInteractionAllowed(previous);
                }
            }, cancellationToken);
        }
        finally { Gate.Release(); }
    }

    [DllImport(Security)] private static extern int SecKeychainGetUserInteractionAllowed(out byte allowed);
    [DllImport(Security)] private static extern int SecKeychainSetUserInteractionAllowed(byte allowed);
    [DllImport(Security)]
    private static extern int SecKeychainFindGenericPassword(IntPtr keychain, uint serviceLength,
        byte[] service, uint accountLength, byte[] account, out uint length, out IntPtr data, IntPtr item);
    [DllImport(Security)] private static extern int SecKeychainItemFreeContent(IntPtr attributes, IntPtr data);
}
