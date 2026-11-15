// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.Core.Application;
using Rodel.MinecraftLauncher.Core.Multiplayer;
using Rodel.MinecraftLauncher.Core.Tests.Infrastructure;
using Rodel.MinecraftLauncher.Tests;

namespace Rodel.MinecraftLauncher.Core.Tests;

/// <summary>Protects manual LAN evidence and native room readiness while publication receipts remain uncertain.</summary>
[TestClass]
[DoNotParallelize]
public sealed class MultiplayerPublicationTests
{
    /// <summary>
    /// Review: existing publication retry cases resolve the receipt before LAN becomes available. They cannot
    /// detect a later unknown receipt hiding a verified invitation, or a late first snapshot resetting a live room.
    /// Keep that receipt unknown through several actual reads and an explicit network retry, then resolve the
    /// original operation. Real TCP/LAN and the shipped component establish readiness independently of the bridge
    /// substitute; receipt counters establish the failure boundary without assuming a sleep reached a refresh.
    /// </summary>
    [TestMethod]
    [TestCategory("Live")]
    [DataRow(false)]
    [DataRow(true)]
    public async Task ManualLan_UnknownPublication_KeepsInvitationAcrossRefreshAndRetry(bool lateWorldObservation)
    {
        RequireLive();
        await ReportStorage.RunAsync(async paths =>
        {
            using var stop = CancellationTokenSource.CreateLinkedTokenSource(TestContext.CancellationToken);
            stop.CancelAfter(TimeSpan.FromSeconds(150));
            var ct = stop.Token;
            var client = new LauncherClient(paths);
            var instance = await client.Instances.CreateAsync("Manual LAN recovery", "26.3", ct);
            await using var bridge = await BridgeFixture.CreateAsync(ct, paths.Root, instance.Id);
            var publication = new PublicationFixture(bridge) { Ready = !lateWorldObservation };
            await using var peer = await bridge.ConnectAsync(publication.Respond, ct);
            MinecraftStatusFixture? game = lateWorldObservation ? new() : null;
            var run = client.Multiplayer.RunAsync(new() { RoomId = Guid.NewGuid().ToString("N"), Role = "host", InstanceId = instance.Id, SessionId = bridge.Session }, ct);
            try
            {
                if (!lateWorldObservation)
                {
                    await WaitAsync(client, run, room => room.State == "manual" && room.ErrorCode == "GAME_OPERATION_OUTCOME_UNKNOWN", ct);
                    game = new();
                }

                var connected = await WaitAsync(client, run, room => room.State == "connected", ct);
                var code = connected.Invitation;
                Assert.IsNotNull(code);
                Assert.AreEqual(game!.Port, connected.GamePort);
                Volatile.Write(ref publication.Ready, true);
                await ObserveConnectedAsync(client, run, publication, code, ct);

                await client.Multiplayer.ControlAsync(connected.Id, "retry", ct: ct);
                var retried = await WaitAsync(client, run, room => room.ControlId != connected.ControlId && room.State == "connected", ct);
                Assert.AreEqual(code, retried.Invitation);
                await ObserveConnectedAsync(client, run, publication, code, ct);
                Assert.AreEqual(1, Volatile.Read(ref publication.Submissions), "Network retry must never replay an uncertain world publication.");
                Assert.AreEqual("unknown", (await bridge.Service.GetOperationAsync(bridge.Session, publication.Operation!, ct)).State);

                publication.AppliedPort = game.Port;
                var applied = await WaitAsync(client, run, room => room.World?.State == "open", ct);
                Assert.AreEqual("connected", applied.State);
                Assert.AreEqual(code, applied.Invitation);
                Assert.AreEqual(1, Volatile.Read(ref publication.Submissions));
                await client.Multiplayer.ControlAsync(applied.Id, "stop", ct: ct);
                Assert.AreEqual("stopped", (await run.WaitAsync(ct)).State);
                Assert.AreEqual("running", client.Sessions.Get(bridge.Session).Status);
            }
            finally { stop.Cancel(); try { await run; } catch (OperationCanceledException) { } if (game is not null) await game.DisposeAsync(); }
        });
    }

    /// <summary>
    /// Review: a TCP listener can be verified while the native component never receives its LAN advertisement.
    /// Withhold only UDP, observe the real discovery deadline and sanitized phase evidence, then enable it and
    /// retry the same room. Existing readiness checks always advertise and cannot distinguish this host failure
    /// from a bad guest code or prove recovery without another game launch.
    /// </summary>
    [TestMethod]
    [TestCategory("Live")]
    public async Task MissingLanAdvertisement_ReportsHostDiscoveryTimeout_AndRecoversSameRoom()
    {
        RequireLive();
        await ReportStorage.RunAsync(async paths =>
        {
            using var stop = CancellationTokenSource.CreateLinkedTokenSource(TestContext.CancellationToken);
            stop.CancelAfter(TimeSpan.FromSeconds(150));
            var ct = stop.Token;
            var client = new LauncherClient(paths);
            var instance = await client.Instances.CreateAsync("LAN discovery", "26.3", ct);
            await using var bridge = await BridgeFixture.CreateAsync(ct, paths.Root, instance.Id);
            await using var game = new MinecraftStatusFixture(broadcast: false);
            var run = client.Multiplayer.RunAsync(new() { RoomId = Guid.NewGuid().ToString("N"), Role = "host", InstanceId = instance.Id, SessionId = bridge.Session, AutoOpen = false }, ct);
            try
            {
                var scanning = await WaitAsync(client, run, room => room.State == "discovering", ct);
                Assert.AreEqual(game.Port, scanning.GamePort);
                Assert.AreEqual("host-scanning", scanning.ComponentState);
                Assert.IsNull(scanning.Invitation);
                var timedOut = await WaitAsync(client, run, room => room.ErrorCode == "MULTIPLAYER_LAN_DISCOVERY_TIMEOUT", ct);
                Assert.AreEqual("disconnected", timedOut.State);
                Assert.AreEqual("host-scanning", timedOut.ComponentState);
                var log = Path.Combine(paths.Root, "discovery.txt");
                await client.Multiplayer.ExportLogAsync(scanning.Id, log, ct);
                var contents = await File.ReadAllTextAsync(log, ct);
                Assert.Contains("host-scanning\ttrue", contents);
                Assert.Contains("MULTIPLAYER_LAN_DISCOVERY_TIMEOUT", contents);
                Assert.DoesNotContain("127.0.0.1", contents);
                Assert.DoesNotContain("U/", contents);

                game.BroadcastEnabled = true;
                await client.Multiplayer.ControlAsync(scanning.Id, "retry", ct: ct);
                var recovered = await WaitAsync(client, run, room => room.State == "connected", ct);
                Assert.AreEqual(scanning.Id, recovered.Id);
                Assert.AreEqual(game.Port, recovered.GamePort);
                Assert.AreEqual(bridge.Session, recovered.SessionId);
                Assert.IsNull(recovered.ErrorCode);
                Assert.IsNotNull(recovered.Invitation);
                await client.Multiplayer.ControlAsync(recovered.Id, "stop", ct: ct);
                Assert.AreEqual("stopped", (await run.WaitAsync(ct)).State);
            }
            finally { stop.Cancel(); try { await run; } catch (OperationCanceledException) { } }
        });
    }

    private async Task ObserveConnectedAsync(LauncherClient client, Task<MultiplayerRoom> run, PublicationFixture publication, string code, CancellationToken ct)
    {
        var target = Volatile.Read(ref publication.ReceiptReads) + 4;
        await WaitAsync(client, run, room =>
        {
            Assert.AreEqual("connected", room.State, "A world receipt must not overwrite verified native network readiness.");
            Assert.AreEqual(code, room.Invitation);
            return Volatile.Read(ref publication.ReceiptReads) >= target;
        }, ct);
    }

    private async Task<MultiplayerRoom> WaitAsync(LauncherClient client, Task<MultiplayerRoom> run, Func<MultiplayerRoom, bool> ready, CancellationToken ct)
    {
        string? previous = null;
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            if (run.IsCompleted) Assert.Fail("The room ended before the expected observation: " + (await run).State);
            if (client.Multiplayer.Get() is { } room)
            {
                var phase = $"{room.State}/{room.ErrorCode}/component={room.ComponentState}/gamePortObserved={room.GamePort is not null}/world={room.World?.State}";
                if (phase != previous) { TestContext.WriteLine(phase); previous = phase; }

                if (ready(room)) return room;
            }

            await Task.Delay(50, ct);
        }
    }

    private static void RequireLive()
    {
        if (Environment.GetEnvironmentVariable("RODEL_MULTIPLAYER_LIVE") != "1") Assert.Inconclusive("Set RODEL_MULTIPLAYER_LIVE=1 to use the shipped component and public discovery services.");
    }

    /// <summary>Gets or sets cooperative cancellation supplied by the test runner.</summary>
    public TestContext TestContext { get; set; }
}