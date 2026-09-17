using Ado.Application;
using Ado.Domain;
using Ado.Platform;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Ado.Tests;

[TestClass]
public sealed class NativeCredentialTests
{
    [TestMethod]
    public void WindowsErrorMappingDistinguishesMissingCredentialAndSession()
    {
        Assert.AreEqual("credential_not_found", NativeCredentialErrors.Windows(1168).Code);
        Assert.AreEqual("credential_provider_unavailable", NativeCredentialErrors.Windows(1312).Code);
        Assert.AreEqual("credential_access_denied", NativeCredentialErrors.Windows(5).Code);
    }

    [TestMethod]
    public void KeychainInteractionErrorExplainsExecutableApproval()
    {
        var error = NativeCredentialErrors.Mac(-25308);
        Assert.AreEqual("interaction_required", error.Code);
        StringAssert.Contains(error.Message, "Always Allow");
        StringAssert.Contains(error.Message, "relocating");
    }

    [TestMethod]
    public async Task ProviderPassesExactLookupAndInteractionPolicy()
    {
        var store = new FakeStore();
        var provider = new NativeCredentialProvider(new Dictionary<string, INativeCredentialStore> { ["macos-keychain"] = store });
        using var secret = await provider.GetAsync(new() { Provider = "macos-keychain", Service = "service", Account = "stable-key" }, true, CancellationToken.None);
        Assert.AreEqual(("service", "stable-key", true), store.Call);
    }

    [TestMethod]
    public async Task MissingLibraryMapsToSafeHeadlessGuidance()
    {
        var provider = new NativeCredentialProvider(new Dictionary<string, INativeCredentialStore> { ["linux-secret-service"] = new FakeStore { Fail = true } });
        var error = await Assert.ThrowsExactlyAsync<AdoException>(() => provider.GetAsync(new() { Provider = "linux-secret-service", Service = "service", Account = "account" }, true, CancellationToken.None));
        Assert.AreEqual("credential_provider_unavailable", error.Code);
        Assert.IsFalse(error.Message.Contains("sensitive-native-error", StringComparison.Ordinal));
    }

    private sealed class FakeStore : INativeCredentialStore
    {
        public bool Fail { get; init; }
        public (string, string, bool) Call { get; private set; }
        public Task<string> ReadAsync(string service, string account, bool nonInteractive, CancellationToken cancellationToken)
        {
            Call = (service, account, nonInteractive);
            if (Fail) throw new DllNotFoundException("sensitive-native-error");
            return Task.FromResult("fake-token");
        }
    }
}
