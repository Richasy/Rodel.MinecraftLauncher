// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.Core.Application;
using System.Text;

namespace Rodel.MinecraftLauncher.Core.Tests;

/// <summary>Protects build-injected Microsoft configuration without accessing Windows accounts or opening authentication UI.</summary>
[TestClass]
public sealed class MicrosoftClientConfigurationTests
{
    /// <summary>
    /// A clean player machine must resolve the Core build's public application ID, not require the developer's environment.
    /// The actual embedded build output is the oracle; an unconfigured developer build must retain AUTH_NOT_CONFIGURED.
    /// Existing recovery/session cases supply their own ID and cannot detect a missing packaged fallback.
    /// </summary>
    [TestMethod]
    public void CleanMachine_UsesInjectedPublicClientOrReportsUnconfiguredDevelopmentBuild()
    {
        using var stream = typeof(LauncherClient).Assembly.GetManifestResourceStream("Rodel.MicrosoftClientId");
        Assert.IsNotNull(stream);
        using var reader = new StreamReader(stream, Encoding.UTF8);
        var configured = reader.ReadToEnd().Trim();

        if (Guid.TryParse(configured, out var expected) && expected != Guid.Empty)
            Assert.AreEqual(expected.ToString(), MicrosoftAuthentication.ResolveClientId(null, null, null));
        else
            Assert.AreEqual("AUTH_NOT_CONFIGURED", Assert.Throws<LauncherOperationException>(() => MicrosoftAuthentication.ResolveClientId(null, null, null)).Code);
    }

    /// <summary>
    /// Explicit, process and current-user overrides must keep their priority over the packaged default.
    /// A malformed explicit override must fail rather than silently authenticate as a different registered application.
    /// Values are supplied at the real configuration boundary so no persistent machine environment is changed.
    /// </summary>
    [TestMethod]
    public void Overrides_KeepPriorityAndRejectInvalidSelectedApplication()
    {
        const string First = "11111111-1111-4111-8111-111111111111";
        const string Second = "22222222-2222-4222-8222-222222222222";
        const string Third = "33333333-3333-4333-8333-333333333333";
        Assert.AreEqual(First, MicrosoftAuthentication.ResolveClientId(First, Second, Third));
        Assert.AreEqual(Second, MicrosoftAuthentication.ResolveClientId(null, Second, Third));
        Assert.AreEqual(Third, MicrosoftAuthentication.ResolveClientId(null, null, Third));
        Assert.AreEqual("AUTH_NOT_CONFIGURED", Assert.Throws<LauncherOperationException>(() => MicrosoftAuthentication.ResolveClientId("invalid", Second, Third)).Code);
        Assert.AreEqual("AUTH_NOT_CONFIGURED", Assert.Throws<LauncherOperationException>(() => MicrosoftAuthentication.ResolveClientId(null, string.Empty, Third)).Code);
    }
}