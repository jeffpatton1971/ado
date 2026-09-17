using System.Runtime.InteropServices;
using System.Text;
using Ado.Application;
using Ado.Platform;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Ado.Tests;

[TestClass]
public sealed class WindowsNativeIntegrationTests
{
    [TestMethod]
    public async Task RoundTripsDisposableSyntheticGenericCredential()
    {
        if (!OperatingSystem.IsWindows()) Assert.Inconclusive("Windows native integration is executed on Windows only.");
        string target = "ado-test-" + Guid.NewGuid().ToString("N");
        const string account = "synthetic-test-account", token = " synthetic-local-test-token ";
        byte[] bytes = Encoding.Unicode.GetBytes(token);
        IntPtr blob = Marshal.AllocHGlobal(bytes.Length);
        bool created = false;
        try
        {
            Marshal.Copy(bytes, 0, blob, bytes.Length);
            var credential = new Credential { Type = 1, TargetName = target, UserName = account, BlobSize = (uint)bytes.Length, Blob = blob, Persist = 1 };
            created = CredWrite(ref credential, 0);
            if (!created) Assert.Inconclusive("This Windows logon session does not allow disposable generic credentials.");
            using var secret = await NativeCredentialProvider.CreateDefault().GetAsync(new()
            {
                Provider = "windows-credential-manager", Service = target, Account = account
            }, true, CancellationToken.None);
            using var auth = new TokenAuthentication("pat", secret);
            using var request = new HttpRequestMessage();
            auth.Apply(request);
            Assert.AreEqual(":" + token, Encoding.UTF8.GetString(Convert.FromBase64String(request.Headers.Authorization!.Parameter!)));
        }
        finally
        {
            Marshal.FreeHGlobal(blob);
            if (created) Assert.IsTrue(CredDelete(target, 1, 0), "Disposable test credential cleanup failed.");
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct Credential
    {
        public uint Flags, Type;
        public string? TargetName, Comment;
        public long LastWritten;
        public uint BlobSize;
        public IntPtr Blob;
        public uint Persist, AttributeCount;
        public IntPtr Attributes;
        public string? TargetAlias, UserName;
    }
    [DllImport("advapi32.dll", EntryPoint = "CredWriteW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)] private static extern bool CredWrite(ref Credential credential, uint flags);
    [DllImport("advapi32.dll", EntryPoint = "CredDeleteW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)] private static extern bool CredDelete(string target, uint type, uint flags);
}
