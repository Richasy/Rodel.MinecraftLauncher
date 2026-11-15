// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.Core.Application;
using Rodel.MinecraftLauncher.Core.Launch;
using System.IO.Compression;
using System.Net;
using System.Text;
using System.Text.Json.Nodes;

namespace Rodel.MinecraftLauncher.Core.Tests;

/// <summary>Crosses real launch planning, protected storage, task recovery and subprocess argument transport; the worker is not Minecraft or a JVM.</summary>
[TestClass]
public sealed class ExternalLaunchTests
{
    private const string Uuid = "0123456789abcdef0123456789abcdef";
    private const string Token = "synthetic-cached-launch-secret";

    /// <summary>
    /// Review: cached launch must use the online UUID/name/token, load the agent before the main class and transport metadata without placing
    /// credentials in session JSON. Authentication tests never start a process, and general Java transport tests never inject this identity.
    /// The independent worker's captured argument sequence detects a misplaced agent, offline fallback or unreplaced secret placeholder.
    /// Only the remote sign-in peer and Java executable are synthetic; planning, monitor, DPAPI and actual process creation are real.
    /// </summary>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task CachedLaunch_AgentPrecedesMainClass_OriginalIdentityReachesProcessWithoutPlaintextSession(bool requestBridge)
    {
        using var java = new JavaProbeFixture(); java.Paths.EnsureDirectories();
        var account = await RegisterAsync(java);
        var client = new LauncherClient(java.Paths);
        var instance = await client.Instances.CreateAsync("External fixture", "fixture", TestContext.CancellationToken);
        await VersionAsync(instance.MinecraftDirectory);
        var plan = await new LaunchPlanner(java.Paths).BuildAsync(instance.MinecraftDirectory, instance.GameDirectory, "fixture", java.Executable,
            account.Name!, 512, [], identity: account, ct: TestContext.CancellationToken);
        Assert.AreEqual(Uuid, plan.OfflineUuid);
        // A requested Java-17 bridge must degrade independently on Java 8 and retain authentication.
        plan = plan with { UseCachedExternalSession = true, JavaMajorVersion = 8, GameBridgeEnabled = requestBridge, GameBridgeMinecraftDirectory = instance.MinecraftDirectory };
        var session = await client.Sessions.CreateAsync(plan, instance.Id, ct: TestContext.CancellationToken);
        await client.Sessions.RunMonitorAsync(session, TestContext.CancellationToken);
        var state = client.Sessions.Get(session);
        Assert.AreEqual("exited", state.Status, state.Error);
        Assert.AreEqual(0, state.ExitCode);
        var calls = Directory.GetFiles(Path.Combine(java.Home, "calls"));
        var arguments = calls.Select(File.ReadAllLines).Single(lines => lines.Contains("fixture.Main"));
        var main = Array.IndexOf(arguments, "fixture.Main");
        var agent = Array.FindIndex(arguments, value => value.StartsWith("-javaagent:", StringComparison.Ordinal));
        Assert.IsTrue(agent >= 0 && agent < main);
        Assert.EndsWith("=http://skin.test/", arguments[agent]);
        var encoded = arguments.Single(value => value.StartsWith("-Dauthlibinjector.yggdrasil.prefetched=", StringComparison.Ordinal)).Split('=', 2)[1];
        Assert.Contains("Cached service", Encoding.UTF8.GetString(Convert.FromBase64String(encoded)));
        Assert.Contains(Uuid, arguments);
        Assert.Contains("CachedPlayer", arguments);
        Assert.Contains(Token, arguments);
        Assert.Contains("mojang", arguments);
        foreach (var file in Directory.GetFiles(java.Paths.SessionsDirectory, "*.json"))
            Assert.DoesNotContain(Token, await File.ReadAllTextAsync(file, TestContext.CancellationToken));
        Assert.AreEqual(account.Id, state.AccountId);
        Assert.IsTrue(state.UseCachedExternalSession);
    }

    /// <summary>
    /// Review: changing the default account or server favorite after queueing must not redirect an explicit recovery attempt.
    /// Rows cover both local and explicit-server admission; a local task that consults the new default loses its original account.
    /// General server launch cases freeze destinations but do not recover authentication failures. A real durable task with a substituted
    /// network failure is sufficient here; the process case above covers execution. Exact account/server assertions distinguish silently
    /// consulting new defaults, while a complete protected cache is required before exposing the cache choice.
    /// </summary>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Recovery_DefaultChanges_RetainsQueuedIdentityAndDestination(bool serverTarget)
    {
        using var java = new JavaProbeFixture(); java.Paths.EnsureDirectories();
        var account = await RegisterAsync(java);
        var client = new LauncherClient(java.Paths);
        var instance = await client.Instances.CreateAsync("Recovery fixture", "fixture", TestContext.CancellationToken);
        await client.Identities.SelectAsync(account.Id, TestContext.CancellationToken);
        var job = await client.Tasks.QueueAsync(new LaunchTask(instance.Id, new() { ServerAddress = serverTarget ? "original.example:25566" : null }), cancellationToken: TestContext.CancellationToken);
        var other = await client.Identities.AddOfflineAsync("NewDefault", TestContext.CancellationToken);
        await client.Identities.SelectAsync(other.Id, TestContext.CancellationToken);
        await Assert.ThrowsAsync<LauncherOperationException>(() => client.Jobs.RunAsync(job.Id!, (_, _) => Task.FromException<JsonNode?>(
            new LauncherOperationException("AUTH_NETWORK_ERROR", "Synthetic service outage")), TestContext.CancellationToken));
        var recovery = client.GetExternalLaunchRecovery(instance.Id, jobId: job.Id);
        Assert.IsNotNull(recovery);
        Assert.AreEqual(account.Id, recovery.Request.Options.AccountId);
        Assert.AreEqual(serverTarget ? "original.example:25566" : null, recovery.Request.Options.ServerAddress);
        Assert.IsFalse(recovery.Request.Options.InheritIdentity);
        Assert.IsFalse(recovery.Request.Options.InheritServer);
        Assert.IsTrue(recovery.CanUseCache);
        Assert.AreEqual(other.Id, client.Identities.GetSelection().AccountId);
    }

    private async Task<IdentityRecord> RegisterAsync(JavaProbeFixture java)
    {
        using var http = new HttpClient(new LoginPeer());
        var service = new IdentityService(java.Paths, http);
        var account = (await service.LoginExternalAsync(new() { Address = "http://skin.test/", Username = "synthetic-user", Password = "synthetic-password" }, TestContext.CancellationToken)).Account!;
        var source = Path.Combine(java.Root, "selected.jar");
        using (var zip = ZipFile.Open(source, ZipArchiveMode.Create))
        {
            using (var writer = new StreamWriter(zip.CreateEntry("META-INF/MANIFEST.MF").Open()))
                await writer.WriteAsync("Premain-Class: moe.yushi.authlibinjector.Premain\r\n");
            _ = zip.CreateEntry("moe/yushi/authlibinjector/Premain.class");
        }

        await service.PrepareExternalLoginComponentAsync(source, TestContext.CancellationToken);
        return account;
    }

    private async Task VersionAsync(string minecraft)
    {
        var folder = Path.Combine(minecraft, "versions", "fixture"); Directory.CreateDirectory(folder);
        await File.WriteAllTextAsync(Path.Combine(folder, "fixture.json"), """{"id":"fixture","mainClass":"fixture.Main","libraries":[],"minecraftArguments":"--username ${auth_player_name} --uuid ${auth_uuid} --accessToken ${auth_access_token} --userType ${user_type}"}""", TestContext.CancellationToken);
        await File.WriteAllBytesAsync(Path.Combine(folder, "fixture.jar"), [], TestContext.CancellationToken);
    }

    private sealed class LoginPeer : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            RequestMessage = request,
            Content = new StringContent(request.RequestUri!.AbsolutePath == "/" ? """{"meta":{"serverName":"Cached service"}}""" :
                $$$"""{"accessToken":"{{{Token}}}","clientToken":"private-client-token","selectedProfile":{"id":"{{{Uuid}}}","name":"CachedPlayer"},"user":{"id":"synthetic-user-id"}}""", Encoding.UTF8, "application/json"),
        });
    }

    /// <summary>Gets cancellation for test-owned file and process operations.</summary>
    public TestContext TestContext { get; set; } = null!;
}