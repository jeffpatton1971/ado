using System.Text;
using System.Text.Json;
using Ado.Application;
using Ado.Domain;
using Ado.Platform;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Ado.Tests;

[TestClass]
public sealed class AuthenticationTests
{
    [TestMethod]
    [DataRow("pat", "Basic")]
    [DataRow("entra-token", "Bearer")]
    public void TokenTypeIsExplicitAndValueIsOpaque(string type, string scheme)
    {
        const string token = " opaque-token ";
        using var auth = new TokenAuthentication(type, new SecretValue(token));
        using var request = new HttpRequestMessage();
        auth.Apply(request);
        Assert.AreEqual(scheme, request.Headers.Authorization!.Scheme);
        Assert.AreEqual(type == "pat" ? Convert.ToBase64String(Encoding.UTF8.GetBytes(":" + token)) : token,
            request.Headers.Authorization.Parameter);
        Assert.AreEqual("[REDACTED]", auth.Redact(token));
    }

    [TestMethod]
    public async Task StdinRemovesOnlyTerminalLineEnding()
    {
        var provider = new InputCredentialProvider(_ => null, new StringReader(" token with spaces \r\n"), _ => throw new AssertFailedException());
        using var secret = await provider.GetAsync(new() { Provider = "stdin" }, true, CancellationToken.None);
        Assert.AreEqual("[REDACTED]", secret.ToString());
        Assert.AreEqual("{}", JsonSerializer.Serialize(secret));
        using var auth = new TokenAuthentication("pat", secret);
        using var request = new HttpRequestMessage();
        auth.Apply(request);
        Assert.AreEqual(": token with spaces ", Encoding.UTF8.GetString(Convert.FromBase64String(request.Headers.Authorization!.Parameter!)));
    }

    [TestMethod]
    public async Task NonInteractiveNeverInvokesPrompt()
    {
        var provider = new InputCredentialProvider(_ => null, TextReader.Null, _ => throw new AssertFailedException());
        var exception = await Assert.ThrowsExactlyAsync<AdoException>(() => provider.GetAsync(new() { Provider = "prompt" }, true, CancellationToken.None));
        Assert.AreEqual("interaction_required", exception.Code);
    }

    [TestMethod]
    public async Task MissingEnvironmentFailsWithoutFallback()
    {
        var provider = new InputCredentialProvider(_ => null, new StringReader("must-not-use"), _ => throw new AssertFailedException());
        var exception = await Assert.ThrowsExactlyAsync<AdoException>(() => provider.GetAsync(new(), false, CancellationToken.None));
        Assert.AreEqual("credential_missing", exception.Code);
    }

    [TestMethod]
    public async Task StdinIsBoundedAndRejectsMultipleLines()
    {
        foreach (string input in new[] { "one\ntwo\n", new string('a', 17000), "" })
        {
            var provider = new InputCredentialProvider(_ => null, new StringReader(input), _ => throw new AssertFailedException());
            await Assert.ThrowsExactlyAsync<AdoException>(() => provider.GetAsync(new() { Provider = "stdin" }, true, CancellationToken.None));
        }
    }
}
