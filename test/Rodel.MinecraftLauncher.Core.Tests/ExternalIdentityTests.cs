// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.Core.Application;
using Rodel.MinecraftLauncher.Core.Tests.Infrastructure;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Rodel.MinecraftLauncher.Core.Tests;

/// <summary>Protects external identity ownership, durable credential boundaries and recovery. Only the remote HTTP peer is substituted.</summary>
[TestClass]
public sealed class ExternalIdentityTests
{
    private const string First = "0123456789abcdef0123456789abcdef";
    private const string Second = "fedcba9876543210fedcba9876543210";
    private const string Token = "synthetic-private-access-token";
    private const string Client = "synthetic-private-client-token";
    private const string Password = "synthetic-password-never-persist";

    /// <summary>
    /// A multi-role login must remain unregistered until selection; cancel must invalidate its handle, and registration must not switch defaults.
    /// Existing account appearance/read tests never cross authentication. Independent file scans and exact returned UUIDs detect premature writes,
    /// plaintext persistence and a selection implementation that silently takes the first role.
    /// </summary>
    [TestMethod]
    public async Task Password_RoleSelectionAndCancellation_PreserveDefaultAndProtectTokens()
    {
        await ReportStorage.RunAsync(async paths =>
        {
            using var http = new HttpClient(new Peer(request => Reply(request.RequestUri!.AbsolutePath switch
            {
                "/site" => "{}",
                "/api/" => """{"meta":{"serverName":"Test service"},"skinDomains":[],"future":{"keep":null}}""",
                "/api/authserver/authenticate" => Session(),
                "/api/authserver/refresh" => Session(Second),
                _ => "{}"
            }, request.RequestUri!.AbsolutePath == "/site" ? new("X-Authlib-Injector-API-Location", "/api/") : null)));
            var service = new IdentityService(paths, http);
            var offline = await service.AddOfflineAsync("Unchanged", TestContext.CancellationToken);
            await service.SelectAsync(offline.Id, TestContext.CancellationToken);
            var canceled = await service.LoginExternalAsync(Login("http://skin.test/site"), TestContext.CancellationToken);
            Assert.AreEqual("roles", canceled.Stage);
            Assert.HasCount(1, service.List());
            await service.CancelExternalLoginAsync(canceled.AttemptId, TestContext.CancellationToken);
            var missing = await Assert.ThrowsAsync<LauncherOperationException>(() => service.SelectExternalProfileAsync(canceled.AttemptId, Second, TestContext.CancellationToken));
            Assert.AreEqual("AUTH_ATTEMPT_UNAVAILABLE", missing.Code);

            var attempt = await service.LoginExternalAsync(Login("http://skin.test/site"), TestContext.CancellationToken);
            var complete = await service.SelectExternalProfileAsync(attempt.AttemptId, Second, TestContext.CancellationToken);
            Assert.AreEqual(Second, complete.Account!.Uuid);
            Assert.AreEqual("http://skin.test/api/", complete.Account.AuthServer);
            Assert.AreEqual(offline.Id, service.GetSelection().AccountId);
            Assert.HasCount(2, service.List());
            var publicJson = JsonSerializer.Serialize(complete, ApplicationJsonContext.Default.ExternalLoginResult);
            foreach (var secret in new[] { Password, Token, Client })
            {
                Assert.IsFalse(publicJson.Contains(secret, StringComparison.Ordinal));
                foreach (var file in Directory.EnumerateFiles(paths.Root, "*.json", SearchOption.AllDirectories))
                    Assert.IsFalse((await File.ReadAllTextAsync(file, TestContext.CancellationToken)).Contains(secret, StringComparison.Ordinal), file);
            }
        });
    }

    /// <summary>
    /// The same UUID from two authentication sources must remain two independently refreshable accounts. A UUID-only registry would replace one
    /// source/token with another; different server names and a refresh request's actual host distinguish that failure without repeating parser tests.
    /// </summary>
    [TestMethod]
    public async Task SameUuid_DifferentServices_RemainIndependent()
    {
        await ReportStorage.RunAsync(async paths =>
        {
            var hosts = new List<string>();
            using var http = new HttpClient(new Peer(request =>
            {
                hosts.Add(request.RequestUri!.Host);
                return Reply(request.RequestUri.AbsolutePath == "/" ? """{"meta":{"serverName":"Origin"}}""" : Session(First));
            }));
            var service = new IdentityService(paths, http);
            var a = (await service.LoginExternalAsync(Login("http://a.test/"), TestContext.CancellationToken)).Account!;
            var b = (await service.LoginExternalAsync(Login("http://b.test/"), TestContext.CancellationToken)).Account!;
            Assert.AreNotEqual(a.Id, b.Id);
            hosts.Clear();
            await service.RefreshExternalAsync(a.Id!, TestContext.CancellationToken);
            Assert.IsTrue(hosts.All(host => host == "a.test"));
            Assert.AreEqual("http://b.test/", service.Get(b.Id!).AuthServer);
            Assert.HasCount(2, service.List());
        });
    }

    /// <summary>
    /// A service failure must not mark a valid identity expired; only a recognized invalid-token result does. This protects recovery choices and
    /// retained registrations, unlike a test of message strings alone. A token echo in the remote diagnostic must also be redacted.
    /// </summary>
    [TestMethod]
    public async Task Refresh_RemoteFailureThenInvalidToken_OnlyExplicitExpiryRequiresLogin()
    {
        await ReportStorage.RunAsync(async paths =>
        {
            var failure = 0;
            using var http = new HttpClient(new Peer(request =>
            {
                if (request.RequestUri!.AbsolutePath == "/") return Reply("""{"meta":{}}""");
                if (failure == 1 && request.RequestUri.AbsolutePath.EndsWith("refresh", StringComparison.Ordinal))
                    return Reply($$"""{"error":"ServiceUnavailable","errorMessage":"Retry later {{Token}}","accessToken":"{{Token}}"}""", status: HttpStatusCode.ServiceUnavailable);
                if (failure == 2 && request.RequestUri.AbsolutePath.EndsWith("refresh", StringComparison.Ordinal))
                    return Reply("""{"error":"ForbiddenOperationException","errorMessage":"Invalid token."}""", status: HttpStatusCode.Forbidden);
                return Reply(Session(First));
            }));
            var service = new IdentityService(paths, http);
            var account = (await service.LoginExternalAsync(Login("http://skin.test/"), TestContext.CancellationToken)).Account!;
            failure = 1;
            var remote = await Assert.ThrowsAsync<LauncherOperationException>(() => service.RefreshExternalAsync(account.Id!, TestContext.CancellationToken));
            Assert.AreEqual("AUTH_REMOTE_ERROR", remote.Code);
            Assert.IsFalse(remote.Message.Contains(Token, StringComparison.Ordinal));
            Assert.IsFalse(remote.Details!.ToJsonString().Contains(Token, StringComparison.Ordinal));
            Assert.AreEqual("ready", service.Get(account.Id!).Status);
            failure = 2;
            var expired = await Assert.ThrowsAsync<LauncherOperationException>(() => service.RefreshExternalAsync(account.Id!, TestContext.CancellationToken));
            Assert.AreEqual("AUTH_LOGIN_REQUIRED", expired.Code);
            Assert.AreEqual("login-required", service.Get(account.Id!).Status);
            Assert.AreEqual(First, service.Get(account.Id!).Uuid);
        });
    }

    /// <summary>A reauthentication bound to a different role must leave the old identity and selected default intact, rather than silently switching UUID.</summary>
    [TestMethod]
    public async Task Reauthentication_DifferentBoundRole_RetainsOriginalIdentity()
    {
        await ReportStorage.RunAsync(async paths =>
        {
            var role = First;
            using var http = new HttpClient(new Peer(request => Reply(request.RequestUri!.AbsolutePath == "/" ? """{"meta":{}}""" : Session(role))));
            var service = new IdentityService(paths, http);
            var account = (await service.LoginExternalAsync(Login("http://skin.test/"), TestContext.CancellationToken)).Account!;
            await service.SelectAsync(account.Id, TestContext.CancellationToken);
            role = Second;
            var error = await Assert.ThrowsAsync<LauncherOperationException>(() => service.LoginExternalAsync(Login("http://skin.test/") with { AccountId = account.Id }, TestContext.CancellationToken));
            Assert.AreEqual("AUTH_PROFILE_REMOVED", error.Code);
            Assert.AreEqual(First, service.Get(account.Id!).Uuid);
            Assert.AreEqual(account.Id, service.GetSelection().AccountId);
            Assert.HasCount(1, service.List());
        });
    }

    /// <summary>
    /// Cancellation is triggered only after the remote device poll has begun. The peer then returns a late successful token despite cancellation;
    /// no account may be committed. This reaches the HTTP completion boundary rather than using a sleep to claim a race was exercised.
    /// </summary>
    [TestMethod]
    public async Task Device_CanceledInFlight_LateTokenCannotRegisterIdentity()
    {
        await ReportStorage.RunAsync(async paths =>
        {
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            using var http = new HttpClient(new Peer(async request =>
            {
                if (request.RequestUri!.AbsolutePath == "/oauth/token")
                {
                    entered.SetResult(); await release.Task;
                    return Reply("""{"access_token":"late-oauth-token"}""");
                }

                return Reply(request.RequestUri.AbsolutePath == "/oauth/device_code"
                    ? """{"user_code":"ABCD-EFGH","device_code":"private-device-code","verification_uri":"https://open.littleskin.cn/oauth/link","expires_in":300,"interval":1}"""
                    : """{"meta":{"serverName":"LittleSkin"}}""");
            }));
            var service = new IdentityService(paths, http);
            var attempt = await service.BeginLittleSkinAsync(clientId: "fixture-client", ct: TestContext.CancellationToken);
            using var cancel = CancellationTokenSource.CreateLinkedTokenSource(TestContext.CancellationToken);
            var polling = service.PollLittleSkinAsync(attempt.AttemptId, cancel.Token);
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.CancellationToken);
            await cancel.CancelAsync(); release.SetResult();
            await Assert.ThrowsAsync<OperationCanceledException>(() => polling);
            await service.CancelExternalLoginAsync(attempt.AttemptId, TestContext.CancellationToken);
            Assert.IsEmpty(service.List());
            Assert.IsEmpty(Directory.EnumerateFiles(Path.Combine(paths.Root, "account-secrets"), "*.json"));
        });
    }

    /// <summary>
    /// The device flow must keep the OAuth bearer separate from the Minecraft token, read actual offered profiles and exchange only the selected UUID.
    /// Neither password tests nor the in-flight cancellation case covers this grant conversion. Request bodies and authorization headers provide
    /// independent evidence; the resulting public JSON and owned files must never expose either private device or OAuth tokens.
    /// Before role confirmation no default may be created; the first verified registration must then become the default.
    /// This extends the existing real registration flow rather than duplicating it with another first-login fixture.
    /// </summary>
    [TestMethod]
    public async Task Device_GrantProfilesAndRoleExchange_RegisterOnlySelectedMinecraftIdentity()
    {
        await ReportStorage.RunAsync(async paths =>
        {
            var exchanged = false;
            using var http = new HttpClient(new Peer(async request =>
            {
                switch (request.RequestUri!.AbsolutePath)
                {
                    case "/api/yggdrasil/": return Reply("""{"meta":{"serverName":"LittleSkin"}}""");
                    case "/oauth/device_code":
                        var fields = await request.Content!.ReadAsStringAsync(TestContext.CancellationToken);
                        Assert.IsTrue(fields.Contains("client_id=fixture-client", StringComparison.Ordinal));
                        Assert.IsTrue(fields.Contains("Yggdrasil.PlayerProfiles.Read", StringComparison.Ordinal));
                        return Reply("""{"user_code":"ABCD-EFGH","device_code":"private-device-code","verification_uri":"https://open.littleskin.cn/oauth/link","expires_in":300,"interval":1}""");
                    case "/oauth/token": return Reply("""{"access_token":"private-oauth-bearer"}""");
                    case "/api/user":
                        Assert.AreEqual("Bearer private-oauth-bearer", request.Headers.Authorization!.ToString());
                        return Reply("""{"uid":42,"email":"must-not-be-stored@example.test"}""");
                    case "/api/yggdrasil/sessionserver/session/minecraft/profile":
                        Assert.AreEqual("Bearer private-oauth-bearer", request.Headers.Authorization!.ToString());
                        return Reply($$"""[{"id":"{{First}}","name":"FirstRole"},{"id":"{{Second}}","name":"SecondRole"}]""");
                    case "/api/yggdrasil/authserver/oauth":
                        Assert.AreEqual("Bearer private-oauth-bearer", request.Headers.Authorization!.ToString());
                        var body = JsonNode.Parse(await request.Content!.ReadAsStringAsync(TestContext.CancellationToken))!;
                        Assert.AreEqual(Second, body["uuid"]!.GetValue<string>());
                        exchanged = true; return Reply(Session(Second));
                    default: return new(HttpStatusCode.NoContent);
                }
            }));
            var service = new IdentityService(paths, http);
            var begin = await service.BeginLittleSkinAsync(clientId: "fixture-client", ct: TestContext.CancellationToken);
            Assert.AreEqual("device", begin.Stage);
            Assert.AreEqual("ABCD-EFGH", begin.UserCode);
            var roles = await service.PollLittleSkinAsync(begin.AttemptId, TestContext.CancellationToken);
            Assert.AreEqual("roles", roles.Stage);
            Assert.IsFalse(exchanged);
            Assert.IsEmpty(service.List());
            Assert.IsNull(service.GetSelection().AccountId);
            var complete = await service.SelectExternalProfileAsync(roles.AttemptId, Second, TestContext.CancellationToken);
            Assert.IsTrue(exchanged);
            Assert.AreEqual(Second, complete.Account!.Uuid);
            Assert.AreEqual("littleskin", complete.Account.AuthMethod);
            Assert.AreEqual(complete.Account.Id, service.GetSelection().AccountId);
            var publicJson = JsonSerializer.Serialize(complete, ApplicationJsonContext.Default.ExternalLoginResult);
            foreach (var secret in stringArray)
            {
                Assert.IsFalse(publicJson.Contains(secret, StringComparison.Ordinal));
                foreach (var file in Directory.EnumerateFiles(paths.Root, "*.json", SearchOption.AllDirectories))
                    Assert.IsFalse((await File.ReadAllTextAsync(file, TestContext.CancellationToken)).Contains(secret, StringComparison.Ordinal));
            }
        });
    }

    /// <summary>
    /// slow_down is a retained waiting state, not a failed login. A second poll is canceled inside the service-requested delay and must not send
    /// another HTTP request. The first poll's explicit response establishes the timing boundary, and the request count detects a missing throttle.
    /// </summary>
    [TestMethod]
    public async Task Device_SlowDown_RetainsCodeAndCancelsBeforeAnotherRequest()
    {
        await ReportStorage.RunAsync(async paths =>
        {
            var polls = 0;
            using var http = new HttpClient(new Peer(request =>
            {
                if (request.RequestUri!.AbsolutePath == "/oauth/token")
                {
                    polls++;
                    return Reply("""{"error":"slow_down"}""", status: HttpStatusCode.BadRequest);
                }

                return Reply(request.RequestUri.AbsolutePath == "/oauth/device_code"
                    ? """{"user_code":"ABCD-EFGH","device_code":"private-device-code","verification_uri":"https://open.littleskin.cn/oauth/link","expires_in":300,"interval":1}"""
                    : """{"meta":{}}""");
            }));
            var service = new IdentityService(paths, http);
            var begin = await service.BeginLittleSkinAsync(clientId: "fixture-client", ct: TestContext.CancellationToken);
            var waiting = await service.PollLittleSkinAsync(begin.AttemptId, TestContext.CancellationToken);
            Assert.AreEqual("device", waiting.Stage);
            Assert.AreEqual(begin.UserCode, waiting.UserCode);
            using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));
            await Assert.ThrowsAsync<OperationCanceledException>(() => service.PollLittleSkinAsync(begin.AttemptId, cancellation.Token));
            Assert.AreEqual(1, polls);
            Assert.AreEqual("device", (await service.GetExternalLoginAsync(begin.AttemptId, TestContext.CancellationToken)).Stage);
            await service.CancelExternalLoginAsync(begin.AttemptId, TestContext.CancellationToken);
        });
    }

    /// <summary>
    /// Review: a remote error can echo a Unicode password in an ordinary JSON value, including a nonstandard scalar error. The existing ASCII token case does not detect a
    /// serialized-escape bypass. Parse the public diagnostic back into independent values: redaction must happen before JSON escaping,
    /// without retaining either the echo or password in the surfaced exception. Only the remote peer is substituted.
    /// </summary>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Password_UnicodeErrorEcho_IsRemovedBeforeDiagnosticSerialization(bool scalar)
    {
        await ReportStorage.RunAsync(async paths =>
        {
            const string password = "密码-合成凭据";
            using var http = new HttpClient(new Peer(request => request.RequestUri!.AbsolutePath == "/" ? Reply("""{"meta":{}}""") :
                Reply(scalar ? JsonValue.Create(password)!.ToJsonString() : new JsonObject { ["errorMessage"] = "Synthetic rejection", ["echo"] = password }.ToJsonString(), status: HttpStatusCode.Forbidden)));
            var service = new IdentityService(paths, http);
            var error = await Assert.ThrowsAsync<LauncherOperationException>(() => service.LoginExternalAsync(new() { Address = "http://skin.test/", Username = "synthetic-user", Password = password }, TestContext.CancellationToken));
            var original = error.Details!["original"]!.ToString();
            var body = JsonNode.Parse(original[(original.IndexOf('\n') + 1)..])!;
            Assert.AreEqual("[redacted]", scalar ? body.GetValue<string>() : body["echo"]!.GetValue<string>());
            Assert.DoesNotContain(password, error.Message);
        });
    }

    private static ExternalLoginOptions Login(string address) => new() { Address = address, Username = "fixture-user", Password = Password };
    private static string Session(string? selected = null) => new JsonObject
    {
        ["accessToken"] = Token,
        ["clientToken"] = Client,
        ["user"] = new JsonObject { ["id"] = "same-owner" },
        ["availableProfiles"] = new JsonArray(new JsonObject { ["id"] = First, ["name"] = "FirstRole" }, new JsonObject { ["id"] = Second, ["name"] = "SecondRole" }),
        ["selectedProfile"] = selected is null ? null : new JsonObject { ["id"] = selected, ["name"] = selected == First ? "FirstRole" : "SecondRole" }
    }.ToJsonString();

    private static HttpResponseMessage Reply(string json, KeyValuePair<string, string>? header = null, HttpStatusCode status = HttpStatusCode.OK)
    {
        var response = new HttpResponseMessage(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
        if (header is { } pair) response.Headers.TryAddWithoutValidation(pair.Key, pair.Value);
        return response;
    }

    private sealed class Peer(Func<HttpRequestMessage, Task<HttpResponseMessage>> reply) : HttpMessageHandler
    {
        internal Peer(Func<HttpRequestMessage, HttpResponseMessage> reply) : this(request => Task.FromResult(reply(request))) { }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => reply(request);
    }

    /// <summary>Gets the runner's cancellation token for real Core work and fixture barriers.</summary>
    public TestContext TestContext { get; set; } = null!;

    private static readonly string[] stringArray = ["private-device-code", "private-oauth-bearer", "must-not-be-stored@example.test"];
}