// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.Core.Application;
using Rodel.MinecraftLauncher.Core.Multiplayer;
using Rodel.MinecraftLauncher.Core.Storage;
using System.Diagnostics;
using System.Text.Json;

namespace Rodel.MinecraftLauncher.Cli.Tests;

/// <summary>Protects CLI admission and protected typed background requests, independently of live peer connectivity.</summary>
[TestClass]
[TestCategory("Integration")]
public sealed class MultiplayerAdmissionTests
{
    private static readonly string[] ExitGameArguments = ["--exit-game"];

    /// <summary>Review: the CLI's legacy stop must retain its network-only scope, while explicit --exit-game must dispatch the typed end action. A live owning process freezes admission before any effect, making the atomic control an independent witness without closing a game.</summary>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Stop_ExitsGameOnlyWithExplicitOption(bool exitGame)
    {
        using var cli = new CliSandbox();
        var directory = Path.Combine(cli.Root, "multiplayer");
        Directory.CreateDirectory(directory);
        using var owner = Process.GetCurrentProcess();
        var room = new MultiplayerRoom
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = "Control fixture",
            Role = "host",
            InstanceId = "fixture",
            Active = true,
            OwnerProcessId = owner.Id,
            OwnerStartedAt = owner.StartTime.ToUniversalTime().ToString("O"),
            State = "connected"
        };
        await JsonFiles.WriteAsync(Path.Combine(directory, "room.json"), JsonSerializer.SerializeToNode(room, MultiplayerJsonContext.Default.MultiplayerRoom)!, TestContext.CancellationToken);
        (await cli.RunAsync(["multiplayer", "stop", room.Id, .. exitGame ? ExitGameArguments : []])).ReadEnvelope(0);
        Assert.AreEqual(exitGame ? "end" : "stop", JsonFiles.ReadObject(Path.Combine(directory, "control.json"))["action"]!.GetValue<string>());
        Assert.IsFalse(owner.HasExited);
    }

    /// <summary>Review: an actual exclusive owner file stops execution before network startup. The real CLI must reject a bad code before queuing, retain a valid invitation only under DPAPI, and persist a non-replayable typed request rather than raw command tokens.</summary>
    [TestMethod]
    public async Task GuestAdmission_ProtectsCodeAndRefusesActiveOwnerWithoutReplay()
    {
        using var cli = new CliSandbox();
        var instance = (await cli.RunAsync("instance", "create", "Guest fixture", "--version", "1.21.1")).ReadEnvelope(0)["data"]!;
        var id = instance["id"]!.GetValue<string>();
        (await cli.RunAsync("multiplayer", "join", id, "--code", "U/7K3M-9V6A-2R8D-5XQ4", "--background")).ReadEnvelope(1);
        var client = new LauncherClient(new LauncherPaths(cli.Root));
        Assert.HasCount(0, client.Jobs.List());
        var directory = Path.Combine(cli.Root, "multiplayer");
        Directory.CreateDirectory(directory);
        using var owner = new FileStream(Path.Combine(directory, "owner.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        const string code = "U/4K3M-9V6A-2R8D-5XQ4";
        await using var job = await JobHost.StartAsync(cli, "multiplayer", "join", id, "--code", code);
        var terminal = await job.WaitForTerminalAsync();
        Assert.AreEqual("failed", terminal["status"]?.ToString());
        Assert.AreEqual("MULTIPLAYER_ROOM_ACTIVE", terminal["failure"]?["error"]?["code"]?.ToString());
        Assert.AreEqual("launcher-task", terminal["request"]?["operation"]?.ToString());
        Assert.AreEqual("multiplayer", terminal["request"]?["parameters"]?["kind"]?.ToString());
        Assert.IsFalse(terminal.ToJsonString().Contains(code, StringComparison.Ordinal));
        var request = (MultiplayerTask)client.Tasks.GetRequest(job.Id);
        Assert.IsTrue(request.Request.StartGame, "Joining starts the game by default after the network is ready.");
        var secret = JsonFiles.ReadObject(Path.Combine(directory, request.Request.RoomId + ".secret.json"));
        Assert.IsNotNull(secret["protected"]);
        Assert.IsFalse(secret.ToJsonString().Contains(code, StringComparison.Ordinal));
        Assert.AreEqual(code, ProtectedStore.Read(Path.Combine(directory, request.Request.RoomId + ".secret.json"))?["invitation"]?.ToString());
        (await cli.RunAsync("job", "resume", job.Id)).ReadEnvelope(1);
        Assert.IsNull(client.Multiplayer.Get());
        Assert.HasCount(1, client.Instances.List());
    }

    public TestContext TestContext { get; set; }

}