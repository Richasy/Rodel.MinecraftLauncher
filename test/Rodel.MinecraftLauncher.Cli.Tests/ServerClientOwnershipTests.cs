using Rodel.MinecraftLauncher.Core.Application;
using Rodel.MinecraftLauncher.Core.Storage;

namespace Rodel.MinecraftLauncher.Cli.Tests;

/// <summary>Protects dedicated-client ownership, inherited launch intent and release without moving player data.</summary>
[TestClass]
[TestCategory("Integration")]
public sealed class ServerClientOwnershipTests
{
    /// <summary>
    /// Ordinary associations must not turn local launches into connections. Explicit dedication changes that
    /// default without copying files; an ordinary-launch override must still work. The real CLI writes the
    /// registrations and Core resolves actual launch intent, without requiring Java or claiming a game joined.
    /// </summary>
    [TestMethod]
    public async Task Bind_ExplicitDedicationOnly_ChangesDefaultLaunchTargetWithoutMovingFiles()
    {
        using var cli = new CliSandbox();
        var created = (await cli.RunAsync("instance", "create", "client", "--version", "fixture")).ReadEnvelope(0)["data"]!;
        var id = created["id"]!.GetValue<string>();
        var game = created["gameDirectory"]!.GetValue<string>();
        var playerFile = Path.Combine(game, "player-file.txt");
        await File.WriteAllTextAsync(playerFile, "preserved", TestContext.CancellationToken);
        (await cli.RunAsync("server", "add", "server", "127.0.0.1:25568", "--id", "owner")).ReadEnvelope(0);
        (await cli.RunAsync("server", "bind", "owner", id, "--preferred")).ReadEnvelope(0);
        var client = new LauncherClient(new LauncherPaths(cli.Root));
        var local = await client.CreateLaunchAsync(id, cancellationToken: TestContext.CancellationToken);
        Assert.IsNull(local.Options.ServerAddress);
        Assert.IsNull(client.Instances.Get(id).ServerId);

        (await cli.RunAsync("server", "bind", "owner", id, "--dedicated")).ReadEnvelope(0);
        var dedicated = await client.CreateLaunchAsync(id, cancellationToken: TestContext.CancellationToken);
        var ordinary = await client.CreateLaunchAsync(id, new() { InheritServer = false }, TestContext.CancellationToken);

        Assert.AreEqual("127.0.0.1:25568", dedicated.Options.ServerAddress);
        Assert.IsFalse(dedicated.Options.InheritServer, "The selected address must not be resolved again by the worker.");
        Assert.IsNull(ordinary.Options.ServerAddress);
        Assert.AreEqual("owner", client.Instances.Get(id).ServerId);
        Assert.AreEqual(game, client.Instances.Get(id).GameDirectory);
        Assert.AreEqual("preserved", await File.ReadAllTextAsync(playerFile, TestContext.CancellationToken));
        Assert.HasCount(1, client.Instances.List());
    }

    /// <summary>
    /// A queued inherited connection must keep the confirmed address and identity even after the favorite is
    /// edited and the client is released. This crosses durable typed queuing rather than merely inspecting an
    /// in-memory option. No worker is started, so the test cannot accidentally launch a game.
    /// </summary>
    [TestMethod]
    public async Task Queue_OwnerChangesAfterSubmission_RetainsFrozenAddressAndIdentity()
    {
        using var cli = new CliSandbox();
        var client = new LauncherClient(new LauncherPaths(cli.Root));
        var instance = await client.Instances.CreateAsync("client", "fixture", TestContext.CancellationToken);
        await client.Servers.AddAsync("server", "127.0.0.1:25568", "owner", TestContext.CancellationToken);
        await client.Servers.SetClientOwnerAsync(instance.Id, "owner", ct: TestContext.CancellationToken);
        await client.Settings.SetAsync("playerName", System.Text.Json.Nodes.JsonValue.Create("BeforeQueue"), instance, TestContext.CancellationToken);
        var queued = await client.Tasks.QueueAsync(new LaunchTask(instance.Id, new()), cancellationToken: TestContext.CancellationToken);

        await client.Servers.AddAsync("server", "127.0.0.1:25569", "owner", TestContext.CancellationToken);
        await client.Settings.SetAsync("playerName", System.Text.Json.Nodes.JsonValue.Create("AfterQueue"), instance, TestContext.CancellationToken);
        await client.Servers.SetClientOwnerAsync(instance.Id, null, ct: TestContext.CancellationToken);
        var frozen = (LaunchTask)client.Tasks.GetRequest(queued.Id!);

        Assert.AreEqual("127.0.0.1:25568", frozen.Options.ServerAddress);
        Assert.AreEqual("BeforeQueue", frozen.Options.PlayerName);
        Assert.IsFalse(frozen.Options.InheritIdentity);
        Assert.IsFalse(frozen.Options.InheritServer);
        Assert.IsNull((await client.CreateLaunchAsync(instance.Id, cancellationToken: TestContext.CancellationToken)).Options.ServerAddress);
    }

    /// <summary>
    /// Removing a server must not strand invisible dedicated clients or delete their worlds/configuration.
    /// Ownership is exclusive: a second server cannot silently take a client before the first is released.
    /// This protects registration lifecycle, separately from launch target selection.
    /// </summary>
    [TestMethod]
    public async Task RemoveServer_ReleasesDedicatedClientsAndPreservesPlayerData()
    {
        using var cli = new CliSandbox();
        var client = new LauncherClient(new LauncherPaths(cli.Root));
        var instance = await client.Instances.CreateAsync("client", "fixture", TestContext.CancellationToken);
        var playerFile = Path.Combine(instance.GameDirectory, "player.txt");
        await File.WriteAllTextAsync(playerFile, "player data", TestContext.CancellationToken);
        await client.Servers.AddAsync("first", "127.0.0.1:25568", "first", TestContext.CancellationToken);
        await client.Servers.AddAsync("second", "127.0.0.1:25569", "second", TestContext.CancellationToken);
        await client.Servers.SetClientOwnerAsync(instance.Id, "first", ct: TestContext.CancellationToken);

        await Assert.ThrowsAsync<InvalidOperationException>(() => client.Servers.SetClientOwnerAsync(instance.Id, "second", ct: TestContext.CancellationToken));
        Assert.AreEqual("first", client.Instances.Get(instance.Id).ServerId);
        (await cli.RunAsync("server", "remove", "first")).ReadEnvelope(0);

        Assert.IsNull(client.Instances.Get(instance.Id).ServerId);
        Assert.HasCount(1, client.Instances.List());
        Assert.AreEqual(instance.GameDirectory, client.Instances.Get(instance.Id).GameDirectory);
        Assert.AreEqual("player data", await File.ReadAllTextAsync(playerFile, TestContext.CancellationToken));
        Assert.IsNull((await client.CreateLaunchAsync(instance.Id, cancellationToken: TestContext.CancellationToken)).Options.ServerAddress);
    }

    /// <summary>Provides the runner cancellation deadline to each isolated ownership scenario.</summary>
    public TestContext TestContext { get; set; } = null!;

    /// <summary>
    /// A Windows read-sharing handle permits reading the favorite but blocks its atomic replacement. Failure
    /// must restore the prior owner instead of leaving a hidden dedicated client whose favorite was not saved.
    /// This exercises a real publication failure, distinct from invalid input rejected before any mutation.
    /// </summary>
    [TestMethod]
    [OSCondition(OperatingSystems.Windows)]
    public async Task Assign_FavoritePublicationBlocked_RestoresLocalOwnership()
    {
        using var cli = new CliSandbox();
        var client = new LauncherClient(new LauncherPaths(cli.Root));
        var instance = await client.Instances.CreateAsync("client", "fixture", TestContext.CancellationToken);
        await client.Servers.AddAsync("server", "127.0.0.1:25568", "owner", TestContext.CancellationToken);
        var before = client.Instances.Get(instance.Id).ToJson().ToJsonString();
        using var held = new FileStream(Path.Combine(cli.Root, "servers.json"), FileMode.Open, FileAccess.Read, FileShare.Read);

        await Assert.ThrowsAsync<IOException>(() => client.Servers.SetClientOwnerAsync(instance.Id, "owner", ct: TestContext.CancellationToken));

        Assert.AreEqual(before, client.Instances.Get(instance.Id).ToJson().ToJsonString());
        Assert.IsNull((await client.Servers.GetAsync("owner", TestContext.CancellationToken)).PreferredInstanceId);
    }
}