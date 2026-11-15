// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.Core.Application;
using Rodel.MinecraftLauncher.Core.Storage;
using Rodel.MinecraftLauncher.Core.Tests.Infrastructure;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;

namespace Rodel.MinecraftLauncher.Core.Tests;

/// <summary>Protects automatic game-session reuse and explicit refresh across real protected account storage.</summary>
[TestClass]
[DoNotParallelize]
public sealed class MicrosoftSessionReuseTests
{
    private const string ClientId = "6124aeac-8b6d-47a6-a54a-512cfadfe654";
    private const string Uuid = "11111111111111111111111111111111";
    private const string AccountId = "microsoft-" + Uuid;
    private const string Token = "synthetic-cached-minecraft-token";

    /// <summary>Gets or sets cancellation for the isolated storage and launch-credential operations.</summary>
    public TestContext TestContext { get; set; } = null!;

    /// <summary>
    /// Repeated launches, including a newly opened service, must use an unexpired protected game session without
    /// requiring Microsoft authentication. The fixture deliberately has no MSAL session, so the previous unconditional
    /// refresh fails locally instead of contacting public services. Existing provider-error tests never enter this path.
    /// Exact returned credentials and unchanged files distinguish reuse from a fabricated launch result or a hidden refresh.
    /// </summary>
    [TestMethod]
    public async Task Launch_ReopensValidSessionWithoutRefreshingOrRewritingAccount()
    {
        await WithSessionAsync(async (paths, secretPath, _) =>
        {
            var secret = await File.ReadAllBytesAsync(secretPath, TestContext.CancellationToken);
            var registrationPath = Path.Combine(paths.Root, "identities.json");
            var registration = await File.ReadAllBytesAsync(registrationPath, TestContext.CancellationToken);

            for (var launch = 0; launch < 2; launch++)
            {
                var credentials = await new IdentityService(paths).GetLaunchCredentialsAsync(AccountId, TestContext.CancellationToken);
                Assert.AreEqual(Token, credentials.MinecraftToken);
                Assert.AreEqual(ClientId, credentials.ClientId);
                // XUID is optional in the existing exchange; its absence must not force another authentication.
                Assert.AreEqual(string.Empty, credentials.Xuid);
            }

            using var canceled = new CancellationTokenSource();
            canceled.Cancel();
            await Assert.ThrowsAsync<OperationCanceledException>(() => new IdentityService(paths).GetLaunchCredentialsAsync(AccountId, canceled.Token));
            CollectionAssert.AreEqual(secret, await File.ReadAllBytesAsync(secretPath, TestContext.CancellationToken));
            CollectionAssert.AreEqual(registration, await File.ReadAllBytesAsync(registrationPath, TestContext.CancellationToken));
        });
    }

    /// <summary>
    /// A stale, incomplete or different player's cache must enter the normal refresh path rather than reach Java.
    /// No MSAL account exists in this isolated browser cache, so refresh deterministically requires login without network
    /// access. This observes the real launch decision and retained recovery state, not a second copy of the expiry predicate.
    /// </summary>
    [TestMethod]
    [DataRow("expired")]
    [DataRow("expires-soon")]
    [DataRow("missing-expiry")]
    [DataRow("invalid-expiry")]
    [DataRow("non-string-expiry")]
    [DataRow("different-player")]
    [DataRow("empty-token")]
    public async Task Launch_UnusableCacheRequiresRefreshAndRetainsCredentials(string fault)
    {
        await WithSessionAsync(async (paths, secretPath, credentials) =>
        {
            switch (fault)
            {
                case "expired": credentials["expiresAt"] = DateTimeOffset.UtcNow.AddMinutes(-10).ToString("O", CultureInfo.InvariantCulture); break;
                case "expires-soon": credentials["expiresAt"] = DateTimeOffset.UtcNow.AddMinutes(2).ToString("O", CultureInfo.InvariantCulture); break;
                case "missing-expiry": credentials.Remove("expiresAt"); break;
                case "invalid-expiry": credentials["expiresAt"] = "not-a-timestamp"; break;
                case "non-string-expiry": credentials["expiresAt"] = 42; break;
                case "different-player": credentials["uuid"] = "22222222222222222222222222222222"; break;
                case "empty-token": credentials["minecraftToken"] = string.Empty; break;
                default: Assert.Fail("Unrecognized cache fault."); break;
            }

            await ProtectedStore.WriteAsync(secretPath, credentials, TestContext.CancellationToken);
            var secret = await File.ReadAllBytesAsync(secretPath, TestContext.CancellationToken);
            var service = new IdentityService(paths);
            var error = await Assert.ThrowsAsync<LauncherOperationException>(() => service.GetLaunchCredentialsAsync(AccountId, TestContext.CancellationToken));

            Assert.AreEqual("AUTH_LOGIN_REQUIRED", error.Code);
            Assert.AreEqual("login-required", new IdentityService(paths).Get(AccountId).Status);
            CollectionAssert.AreEqual(secret, await File.ReadAllBytesAsync(secretPath, TestContext.CancellationToken));
        });
    }

    /// <summary>
    /// Explicit account refresh must still authenticate even when the game token has time left. Once that refresh requires
    /// reauthorization, an automatic launch must not revive the old ready state merely because its cached expiry is later.
    /// Missing MSAL state is the only provider boundary here; protected files and recovery classification remain real.
    /// </summary>
    [TestMethod]
    public async Task ExplicitRefresh_RequiresAuthenticationAndLaunchHonorsRejectedSession()
    {
        await WithSessionAsync(async (paths, secretPath, _) =>
        {
            var secret = await File.ReadAllBytesAsync(secretPath, TestContext.CancellationToken);
            var service = new IdentityService(paths);
            var refresh = await Assert.ThrowsAsync<LauncherOperationException>(() => service.RefreshMicrosoftAsync(AccountId, TestContext.CancellationToken));
            Assert.AreEqual("AUTH_LOGIN_REQUIRED", refresh.Code);
            Assert.AreEqual("login-required", service.Get(AccountId).Status);

            var launch = await Assert.ThrowsAsync<LauncherOperationException>(() => new IdentityService(paths).GetLaunchCredentialsAsync(AccountId, TestContext.CancellationToken));
            Assert.AreEqual("AUTH_LOGIN_REQUIRED", launch.Code);
            CollectionAssert.AreEqual(secret, await File.ReadAllBytesAsync(secretPath, TestContext.CancellationToken));
        });
    }

    private async Task WithSessionAsync(Func<LauncherPaths, string, JsonObject, Task> action)
    {
        // Client configuration is process-scoped; this class is serialized and always restores the previous value.
        var previousClient = Environment.GetEnvironmentVariable("RODEL_MICROSOFT_CLIENT_ID");
        Environment.SetEnvironmentVariable("RODEL_MICROSOFT_CLIENT_ID", ClientId);
        try
        {
            await ReportStorage.RunAsync(async paths =>
            {
                var identity = new JsonObject { ["id"] = AccountId, ["uuid"] = Uuid, ["name"] = "CachedPlayer", ["type"] = "microsoft", ["status"] = "ready" };
                await JsonFiles.WriteAsync(Path.Combine(paths.Root, "identities.json"), new JsonObject { ["identities"] = new JsonArray(identity) }, TestContext.CancellationToken);
                var credentials = new JsonObject
                {
                    ["clientId"] = ClientId,
                    ["method"] = "Browser",
                    ["minecraftToken"] = Token,
                    ["uuid"] = Uuid,
                    ["expiresAt"] = DateTimeOffset.UtcNow.AddHours(2).ToString("O", CultureInfo.InvariantCulture)
                };
                var secretPath = Path.Combine(paths.Root, "account-secrets", Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(AccountId))) + ".json");
                await ProtectedStore.WriteAsync(secretPath, credentials, TestContext.CancellationToken);
                await action(paths, secretPath, credentials);
            });
        }
        finally { Environment.SetEnvironmentVariable("RODEL_MICROSOFT_CLIENT_ID", previousClient); }
    }
}