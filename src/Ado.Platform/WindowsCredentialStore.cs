using System.Runtime.InteropServices;
using Ado.Domain;

namespace Ado.Platform;

internal sealed class WindowsCredentialStore : INativeCredentialStore
{
    public Task<string> ReadAsync(string service, string account, bool nonInteractive, CancellationToken cancellationToken) => Task.Run(() =>
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!CredRead(service, 1, 0, out var pointer)) throw NativeCredentialErrors.Windows(Marshal.GetLastWin32Error());
        try
        {
            var credential = Marshal.PtrToStructure<Credential>(pointer);
            if (!string.Equals(Marshal.PtrToStringUni(credential.UserName), account, StringComparison.Ordinal))
                throw new AdoException("credential_account_mismatch", "The Windows credential account does not match the configured lookup key.", ExitCode.Authentication);
            if (credential.BlobSize == 0 || credential.BlobSize > 32768 || credential.BlobSize % 2 != 0)
                throw new AdoException("invalid_credential", "The Windows credential must contain a bounded UTF-16 password.", ExitCode.Authentication);
            cancellationToken.ThrowIfCancellationRequested();
            return Marshal.PtrToStringUni(credential.Blob, checked((int)credential.BlobSize / 2))!;
        }
        finally { CredFree(pointer); }
    }, cancellationToken);

    [StructLayout(LayoutKind.Sequential)]
    private struct Credential
    {
        public uint Flags, Type;
        public IntPtr TargetName, Comment;
        public long LastWritten;
        public uint BlobSize;
        public IntPtr Blob;
        public uint Persist, AttributeCount;
        public IntPtr Attributes, TargetAlias, UserName;
    }

    [DllImport("advapi32.dll", EntryPoint = "CredReadW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredRead(string target, uint type, uint flags, out IntPtr credential);
    [DllImport("advapi32.dll")]
    private static extern void CredFree(IntPtr buffer);
}
