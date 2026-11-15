// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.Core.Application;
using Rodel.MinecraftLauncher.Core.Storage;
using Rodel.MinecraftLauncher.Core.Tests.Infrastructure;
using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;

namespace Rodel.MinecraftLauncher.Core.Tests;

/// <summary>Protects optional skin metadata refresh against credential changes, provider confusion and late commits; HTTP alone is substituted.</summary>
[TestClass]
public sealed class AccountSkinRefreshTests
{
    private const string Uuid = "0123456789abcdef0123456789abcdef";
    private const string OldSkin = "https://textures.minecraft.net/texture/old";
    private const string NewSkin = "https://textures.minecraft.net/texture/new";
    private const string Token = "synthetic-game-token";

    /// <summary>Gets runner cancellation for filesystem work and controlled HTTP barriers.</summary>
    public TestContext TestContext { get; set; } = null!;

    /// <summary>Review: existing authentication tests renew sessions. This query must change only skin metadata, including an explicit reset, while preserving actual DPAPI bytes, login state, name and unknown data.</summary>
    [TestMethod]
    [DataRow("microsoft", false)]
    [DataRow("microsoft", true)]
    [DataRow("external", false)]
    [DataRow("external", true)]
    public async Task Query_ChangedOrResetSkin_OnlyUpdatesSkin(string type, bool reset)
    {
        await ReportStorage.RunAsync(async paths =>
        {
            var (account, secretPath) = await SeedAsync(paths, type);
            var secret = await File.ReadAllBytesAsync(secretPath, TestContext.CancellationToken);
            var requests = 0;
            using var http = new HttpClient(new Peer(request =>
            {
                requests++;
                Assert.AreEqual(HttpMethod.Get, request.Method);
                Assert.AreEqual(type == "microsoft" ? "api.minecraftservices.com" : "skin.test", request.RequestUri!.Host);
                Assert.AreEqual(type == "microsoft" ? "/minecraft/profile" : "/api/sessionserver/session/minecraft/profile/" + Uuid, request.RequestUri.AbsolutePath);
                Assert.AreEqual(type == "microsoft" ? Token : null, request.Headers.Authorization?.Parameter);
                return Reply(type == "microsoft" ? MicrosoftProfile(reset ? null : NewSkin) : RoleProfile(reset ? null : NewSkin));
            }));
            var result = await new IdentityService(paths, http).RefreshSkinAsync(account.Id!, TestContext.CancellationToken);
            Assert.AreEqual(1, requests);
            Assert.AreEqual(reset ? null : NewSkin, result.SkinUrl);
            var stored = new IdentityService(paths).Get(account.Id!);
            Assert.AreEqual(result.SkinUrl, stored.SkinUrl);
            Assert.AreEqual(account.Status, stored.Status);
            Assert.AreEqual(account.Name, stored.Name);
            Assert.AreEqual(account.VerifiedAt, stored.VerifiedAt);
            Assert.AreEqual(42, stored.AdditionalData["future"].GetInt32());
            CollectionAssert.AreEqual(secret, await File.ReadAllBytesAsync(secretPath, TestContext.CancellationToken));
        });
    }

    /// <summary>Review: expired or rejected game credentials must not trigger authentication just to update an avatar. A public UUID query remains possible and must never carry the bearer.</summary>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Microsoft_UnavailableBearer_UsesPublicProfileWithoutChangingLogin(bool rejected)
    {
        await ReportStorage.RunAsync(async paths =>
        {
            var (account, secretPath) = await SeedAsync(paths, "microsoft");
            if (!rejected) File.Delete(secretPath);
            var bearerRequests = 0;
            var publicRequests = 0;
            using var http = new HttpClient(new Peer(request =>
            {
                if (request.RequestUri!.Host == "api.minecraftservices.com")
                {
                    bearerRequests++;
                    return Reply("{}", HttpStatusCode.Unauthorized);
                }

                publicRequests++;
                Assert.AreEqual("https://sessionserver.mojang.com/session/minecraft/profile/" + Uuid, request.RequestUri.AbsoluteUri);
                Assert.IsNull(request.Headers.Authorization);
                return Reply(RoleProfile(NewSkin));
            }));
            var result = await new IdentityService(paths, http).RefreshSkinAsync(account.Id!, TestContext.CancellationToken);
            Assert.AreEqual(rejected ? 1 : 0, bearerRequests);
            Assert.AreEqual(1, publicRequests);
            Assert.AreEqual(NewSkin, result.SkinUrl);
            Assert.AreEqual("ready", result.Status);
            Assert.AreEqual(rejected, File.Exists(secretPath));
        });
    }

    /// <summary>Review: a missing role or malformed texture is not a confirmed default skin. HTTP failures and invalid payloads must leave the complete registry unchanged; tolerant login parsing must not accidentally erase an avatar here.</summary>
    [TestMethod]
    [DataRow("unavailable")]
    [DataRow("missing-role")]
    [DataRow("different-role")]
    [DataRow("malformed-texture")]
    [DataRow("invalid-url")]
    public async Task Query_Failure_PreservesStoredIdentity(string fault)
    {
        await ReportStorage.RunAsync(async paths =>
        {
            var (account, _) = await SeedAsync(paths, "external");
            var file = Path.Combine(paths.Root, "identities.json");
            var before = await File.ReadAllBytesAsync(file, TestContext.CancellationToken);
            using var http = new HttpClient(new Peer(_ => fault switch
            {
                "unavailable" => Reply("{}", HttpStatusCode.ServiceUnavailable),
                "missing-role" => Reply(string.Empty, HttpStatusCode.NoContent),
                "different-role" => Reply(RoleProfile(NewSkin).Replace(Uuid, "fedcba9876543210fedcba9876543210", StringComparison.Ordinal)),
                "malformed-texture" => Reply($$"""{"id":"{{Uuid}}","name":"Remote","properties":[{"name":"textures","value":"not-base64"}]}"""),
                _ => Reply(RoleProfile("file:///C:/unexpected.png"))
            }));
            await Assert.ThrowsAsync<Exception>(() => new IdentityService(paths, http).RefreshSkinAsync(account.Id!, TestContext.CancellationToken));
            CollectionAssert.AreEqual(before, await File.ReadAllBytesAsync(file, TestContext.CancellationToken));
        });
    }

    /// <summary>Review: hold the actual HTTP response while removing or reauthenticating the same role. Registry operations must finish before release, and the delayed skin response must not resurrect a removed account or overwrite newer login data.</summary>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Query_ConcurrentIdentityChange_NewerStateWins(bool remove)
    {
        await ReportStorage.RunAsync(async paths =>
        {
            var (account, _) = await SeedAsync(paths, "external");
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            using var http = new HttpClient(new Peer(async _ =>
            {
                entered.TrySetResult();
                await release.Task.WaitAsync(TestContext.CancellationToken);
                return Reply(RoleProfile(NewSkin));
            }));
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(TestContext.CancellationToken);
            deadline.CancelAfter(TimeSpan.FromSeconds(10));
            var service = new IdentityService(paths, http);
            var refresh = service.RefreshSkinAsync(account.Id!, deadline.Token);
            try
            {
                await entered.Task.WaitAsync(deadline.Token);
                if (remove)
                {
                    await service.RemoveAsync(account.Id!, deadline.Token);
                }
                else
                {
                    var file = Path.Combine(paths.Root, "identities.json");
                    using var lease = await ResourceLease.AcquireAsync(file, deadline.Token);
                    var document = JsonFiles.ReadObject(file);
                    document["identities"]![0]!["verifiedAt"] = "2026-11-03T12:00:00Z";
                    document["identities"]![0]!["skinUrl"] = "https://textures.minecraft.net/texture/newer-login";
                    await JsonFiles.WriteAsync(file, document, deadline.Token);
                }
            }
            finally { release.TrySetResult(); }

            if (remove)
            {
                var error = await Assert.ThrowsAsync<LauncherOperationException>(() => refresh);
                Assert.AreEqual("ACCOUNT_NOT_FOUND", error.Code);
                Assert.IsEmpty(service.List());
            }
            else
            {
                Assert.AreEqual("https://textures.minecraft.net/texture/newer-login", (await refresh).SkinUrl);
                Assert.AreEqual("https://textures.minecraft.net/texture/newer-login", service.Get(account.Id!).SkinUrl);
            }
        });
    }

    private async Task<(IdentityRecord Account, string SecretPath)> SeedAsync(LauncherPaths paths, string type)
    {
        var id = type + "-" + Uuid;
        var identity = new JsonObject
        {
            ["id"] = id,
            ["uuid"] = Uuid,
            ["name"] = "RetainedName",
            ["type"] = type,
            ["status"] = "ready",
            ["skinUrl"] = OldSkin,
            ["verifiedAt"] = "2026-01-01T00:00:00Z",
            ["future"] = 42,
            ["authServer"] = type == "external" ? "https://skin.test/api/" : null
        };
        await JsonFiles.WriteAsync(Path.Combine(paths.Root, "identities.json"), new JsonObject { ["identities"] = new JsonArray(identity) }, TestContext.CancellationToken);
        var secretPath = Path.Combine(paths.Root, "account-secrets", Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(id))) + ".json");
        await ProtectedStore.WriteAsync(secretPath, new JsonObject
        {
            ["minecraftToken"] = Token,
            ["uuid"] = Uuid,
            ["expiresAt"] = DateTimeOffset.UtcNow.AddHours(2).ToString("O", CultureInfo.InvariantCulture)
        }, TestContext.CancellationToken);
        return (new IdentityService(paths).Get(id), secretPath);
    }

    private static string MicrosoftProfile(string? skin) => new JsonObject
    {
        ["id"] = Uuid,
        ["name"] = "RemoteName",
        ["skins"] = skin is null ? [] : new JsonArray(new JsonObject { ["state"] = "ACTIVE", ["url"] = skin })
    }.ToJsonString();

    private static string RoleProfile(string? skin) => new JsonObject
    {
        ["id"] = Uuid,
        ["name"] = "RemoteName",
        ["properties"] = new JsonArray(new JsonObject
        {
            ["name"] = "textures",
            ["value"] = Convert.ToBase64String(Encoding.UTF8.GetBytes(new JsonObject
            {
                ["textures"] = skin is null ? [] : new JsonObject { ["SKIN"] = new JsonObject { ["url"] = skin } }
            }.ToJsonString()))
        })
    }.ToJsonString();

    private static HttpResponseMessage Reply(string body, HttpStatusCode status = HttpStatusCode.OK) => new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private sealed class Peer(Func<HttpRequestMessage, Task<HttpResponseMessage>> reply) : HttpMessageHandler
    {
        internal Peer(Func<HttpRequestMessage, HttpResponseMessage> reply) : this(request => Task.FromResult(reply(request))) { }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => reply(request);
    }
}