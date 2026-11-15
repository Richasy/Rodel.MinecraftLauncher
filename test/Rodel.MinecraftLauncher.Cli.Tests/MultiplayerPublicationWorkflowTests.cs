// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.Core.Application;
using Rodel.MinecraftLauncher.Core.Multiplayer;
using Rodel.MinecraftLauncher.Core.Storage;
using Rodel.MinecraftLauncher.Tests;

namespace Rodel.MinecraftLauncher.Cli.Tests;

/// <summary>Runs uncertain LAN publication through the actual CLI job owner and generated room responses.</summary>
[TestClass]
[TestCategory("Live")]
[DoNotParallelize]
public sealed class MultiplayerPublicationWorkflowTests
{
    /// <summary>
    /// Review: in-process room checks cannot prove the shipped CLI's job lifetime or NativeAOT JSON metadata.
    /// Start the actual CLI host, retain an unknown game receipt while the native component hosts the verified
    /// fixture, retry, and read/export through CLI commands. The game transport alone is substituted; this also
    /// accepts RODEL_TEST_CLI_EXECUTABLE for the normal published NativeAOT executable.
    /// </summary>
    [TestMethod]
    public async Task Host_UnknownPublication_PreservesCodeAndExportsIndependentPhases()
    {
        if (Environment.GetEnvironmentVariable("RODEL_MULTIPLAYER_LIVE") != "1") Assert.Inconclusive("Set RODEL_MULTIPLAYER_LIVE=1 to use the shipped network component.");
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(TestContext.CancellationToken);
        stop.CancelAfter(TimeSpan.FromSeconds(150));
        var ct = stop.Token;
        using var cli = new CliSandbox();
        var observer = new LauncherClient(new LauncherPaths(cli.Root));
        var instance = (await cli.RunAsync("instance", "create", "Native LAN", "--version", "26.3")).ReadEnvelope(0)["data"]!;
        await using var bridge = await BridgeFixture.CreateAsync(ct, cli.Root, instance["id"]!.GetValue<string>());
        var publication = new PublicationFixture(bridge) { Ready = true };
        await using var peer = await bridge.ConnectAsync(publication.Respond, ct);
        await using var job = await JobHost.StartAsync(cli, "multiplayer", "host", instance["id"]!.GetValue<string>(), "--session", bridge.Session);
        await WaitAsync(observer, room => room.State == "manual", ct);
        await using var game = new MinecraftStatusFixture();
        var connected = await WaitAsync(observer, room => room.State == "connected", ct);
        Assert.IsNotNull(connected.Invitation);
        var reads = Volatile.Read(ref publication.ReceiptReads) + 4;
        await WaitAsync(observer, room =>
        {
            Assert.AreEqual("connected", room.State);
            Assert.AreEqual(connected.Invitation, room.Invitation, "A repeated receipt hid the live invitation.");
            return Volatile.Read(ref publication.ReceiptReads) >= reads;
        }, ct);
        (await cli.RunAsync("multiplayer", "retry", connected.Id)).ReadEnvelope(0);
        await WaitAsync(observer, room => room.ControlId != connected.ControlId && room.State == "connected", ct);
        var shown = (await cli.RunAsync("multiplayer", "show")).ReadEnvelope(0)["data"]!;
        Assert.AreEqual("connected", shown["state"]?.ToString());
        Assert.AreEqual("host-ok", shown["componentState"]?.ToString());
        Assert.AreEqual(connected.Invitation, shown["invitation"]?.ToString());
        Assert.AreEqual(game.Port, shown["gamePort"]?.GetValue<int>());
        Assert.AreEqual(1, Volatile.Read(ref publication.Submissions));
        var log = Path.Combine(cli.Root, "connection.txt");
        (await cli.RunAsync("multiplayer", "export-log", connected.Id, log)).ReadEnvelope(0);
        var contents = await File.ReadAllTextAsync(log, ct);
        Assert.Contains("host-ok\ttrue\tunknown", contents);
        Assert.DoesNotContain(connected.Invitation!, contents);
        (await cli.RunAsync("multiplayer", "stop", connected.Id)).ReadEnvelope(0);
        Assert.AreEqual("succeeded", (await job.WaitForTerminalAsync())["status"]?.ToString());
        Assert.AreEqual("running", observer.Sessions.Get(bridge.Session).Status);
    }

    private async Task<MultiplayerRoom> WaitAsync(LauncherClient observer, Func<MultiplayerRoom, bool> ready, CancellationToken ct)
    {
        string? previous = null;
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            if (observer.Multiplayer.Get() is { } room)
            {
                var phase = $"{room.State}/{room.ErrorCode}/component={room.ComponentState}/gamePortObserved={room.GamePort is not null}";
                if (phase != previous) { TestContext.WriteLine(phase); previous = phase; }

                if (ready(room)) return room;
            }

            await Task.Delay(50, ct);
        }
    }

    /// <summary>Gets or sets cooperative cancellation supplied by the test runner.</summary>
    public TestContext TestContext { get; set; }
}